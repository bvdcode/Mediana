// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal class JpegDocument(ReadOnlyMemory<byte> encoded, JpegDecodeOptions options, JpegColorSpace? sourceColorSpace,
        SampleAspectRatio? sampleAspectRatio, CancellationToken token, int reduction = 1) : IDisposable
    {
        private readonly JpegMarkerReader reader = new(encoded, options.MaximumMarkers, token);
        private readonly int[][] quantization = new int[4][];
        private readonly bool[] wideQuantization = new bool[4];
        private readonly bool[] lockedQuantization = new bool[4];
        private readonly JpegHuffmanTable?[] huffman = new JpegHuffmanTable[8];
        private JpegComponent[] components = [];
        private readonly JpegMetadata metadata = new();
        private int restartInterval;
        private byte frameMarker;
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal int MaximumHorizontal { get; private set; }
        internal int MaximumVertical { get; private set; }
        internal IReadOnlyList<JpegComponent> Components => components;
        internal JpegMetadata Metadata => metadata;
        internal bool Progressive => frameMarker == 194;
        internal IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();

        private void AddWarning(string warning) => Warnings = Array.AsReadOnly<string>([.. Warnings, warning]);

        internal void RecoverFinalRestart(int expected)
        {
            ReadOnlySpan<byte> remaining = reader.Encoded.Span[reader.Position..];
            if (remaining.Length == 4 && remaining[0] == 255 && remaining[1] == 208 + expected
                && remaining[2] == 255 && remaining[3] == 217)
            {
                reader.NextMarker();
                AddWarning("Ignored a redundant final JPEG restart marker immediately before EOI.");
            }
        }

        internal void Read(MediaBufferAllocator? allocator, bool componentSamples = false)
        {
            token.ThrowIfCancellationRequested();
            if (encoded.Length > options.MaximumEncodedBytes)
            {
                throw new InvalidDataException("JPEG exceeds MaximumEncodedBytes.");
            }
            if (reader.NextMarker() != 216)
            {
                throw new InvalidDataException("JPEG must begin with SOI.");
            }
            while (true)
            {
                byte marker = reader.NextMarker();
                if (marker == 217)
                {
                    if (components.Length == 0 || components.Any(component => !component.Decoded))
                    {
                        throw new InvalidDataException("JPEG ends before every component has been scanned.");
                    }
                    if (reader.Position != encoded.Length)
                    {
                        throw new InvalidDataException("JPEG contains bytes after EOI.");
                    }
                    if (!componentSamples)
                    {
                        metadata.Validate(components, sourceColorSpace, sampleAspectRatio, allocator is not null, options.ChromaConflictPolicy);
                        if (metadata.UsedJfifChromaPosition)
                        {
                            AddWarning("JPEG JFIF and Exif chroma positions conflict; using centered JFIF chroma positioning.");
                        }
                    }
                    if (metadata.HasUnalignedExifOffsets)
                    {
                        AddWarning("JPEG Exif contains unaligned offsets; reading values at their declared byte positions.");
                    }
                    if (Progressive && allocator is not null)
                    {
                        foreach (JpegComponent component in components)
                        {
                            component.Progression!.Reconstruct(component, GetQuantization(component), allocator, token);
                        }
                    }
                    return;
                }
                if (marker is 216 or 1 or >= 208 and <= 215)
                {
                    throw new InvalidDataException("Unexpected standalone JPEG marker.");
                }
                ReadOnlySpan<byte> segment = reader.ReadSegment();
                switch (marker)
                {
                    case 192:
                    case 193:
                    case 194:
                        ReadFrame(marker, segment, allocator);
                        break;
                    case 196:
                        ReadHuffman(segment);
                        break;
                    case 219:
                        ReadQuantization(segment);
                        break;
                    case 221:
                        if (segment.Length != 2)
                        {
                            throw new InvalidDataException("JPEG DRI length is invalid.");
                        }
                        restartInterval = BinaryPrimitives.ReadUInt16BigEndian(segment);
                        break;
                    case 218:
                        JpegScanReader.Read(segment, reader, this, allocator is not null, restartInterval, token);
                        break;
                    case >= 224 and <= 239:
                        if (!componentSamples || marker == 238)
                        {
                            metadata.Read(marker, encoded.Slice(reader.Position - segment.Length, segment.Length), options.AllowUnalignedExifOffsets, token);
                            if (allocator is null)
                            {
                                metadata.Inspect(marker, encoded.Slice(reader.Position - segment.Length, segment.Length), token);
                            }
                        }
                        break;
                    case 254:
                        if (allocator is null)
                        {
                            metadata.Inspect(marker, encoded.Slice(reader.Position - segment.Length, segment.Length), token);
                        }
                        break;
                    case 220:
                        throw new NotSupportedException("JPEG deferred image height (DNL) is not supported.");
                    default:
                        throw new NotSupportedException($"JPEG marker 0xFF{marker:X2} is not supported.");
                }
            }
        }

        internal int[] GetQuantization(JpegComponent component)
        {
            int[]? values = quantization[component.Quantization];
            if (values is null)
            {
                throw new InvalidDataException("JPEG component refers to an undefined quantization table.");
            }
            if (frameMarker == 192 && wideQuantization[component.Quantization])
            {
                throw new InvalidDataException("Baseline JPEG requires 8-bit quantization tables.");
            }
            return values;
        }

        internal JpegHuffmanTable GetHuffman(int index)
        {
            if (frameMarker == 192 && (index & 3) > 1)
            {
                throw new InvalidDataException("Baseline JPEG requires Huffman table selectors zero or one.");
            }
            return huffman[index] ?? throw new InvalidDataException("JPEG scan refers to an undefined Huffman table.");
        }

        internal void LockQuantization(JpegComponent component) => lockedQuantization[component.Quantization] = true;

        internal JpegSampleLayout TakeSamples()
        {
            JpegSampleLayout result = new((Width + reduction - 1) / reduction, (Height + reduction - 1) / reduction,
                MaximumHorizontal, MaximumVertical, components);
            components = [];
            return result;
        }

        public void Dispose()
        {
            foreach (JpegComponent? component in components)
            {
                component?.Samples?.Dispose();
                component?.Progression?.Dispose();
            }
        }

        private void ReadFrame(byte marker, ReadOnlySpan<byte> segment, MediaBufferAllocator? allocator)
        {
            if (frameMarker != 0 || segment.Length < 6)
            {
                throw new InvalidDataException("JPEG frame header is duplicated or truncated.");
            }
            if (segment[0] != 8)
            {
                throw new NotSupportedException("JPEG decoding currently requires 8-bit sample precision.");
            }
            Height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
            Width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
            if (Height == 0)
            {
                throw new NotSupportedException("JPEG deferred image height (DNL) is not supported.");
            }
            if (Width == 0 || (long)Width * Height > options.MaximumPixels)
            {
                throw new InvalidDataException("JPEG dimensions are invalid or exceed MaximumPixels.");
            }
            int count = segment[5];
            if (segment.Length != 6 + count * 3 || count == 0)
            {
                throw new InvalidDataException("JPEG frame component count or length is invalid.");
            }
            if (count is not (1 or 3))
            {
                throw new NotSupportedException("JPEG decoding currently supports grayscale and three-component color.");
            }
            components = new JpegComponent[count];
            for (int i = 0; i < count; i++)
            {
                int offset = 6 + i * 3;
                byte id = segment[offset];
                int horizontal = segment[offset + 1] >> 4;
                int vertical = segment[offset + 1] & 15;
                int table = segment[offset + 2];
                if (horizontal is < 1 or > 4 || vertical is < 1 or > 4 || table > 3)
                {
                    throw new InvalidDataException("JPEG sampling factors or quantization selector are invalid.");
                }
                for (int j = 0; j < i; j++)
                {
                    if (components[j].Id == id)
                    {
                        throw new InvalidDataException("JPEG frame component identifiers must be unique.");
                    }
                }
                components[i] = new(id, horizontal, vertical, table, reduction);
                MaximumHorizontal = Math.Max(MaximumHorizontal, horizontal);
                MaximumVertical = Math.Max(MaximumVertical, vertical);
            }
            frameMarker = marker;
            int mcuColumns = (Width + MaximumHorizontal * 8 - 1) / (MaximumHorizontal * 8);
            int mcuRows = (Height + MaximumVertical * 8 - 1) / (MaximumVertical * 8);
            foreach (JpegComponent component in components)
            {
                component.Width = (Width * component.Horizontal + MaximumHorizontal - 1) / MaximumHorizontal;
                component.Height = (Height * component.Vertical + MaximumVertical - 1) / MaximumVertical;
                component.Stride = checked(mcuColumns * component.Horizontal * 8);
                component.PaddedHeight = checked(mcuRows * component.Vertical * 8);
                component.SampleWidth = (component.Width + reduction - 1) / reduction;
                component.SampleHeight = (component.Height + reduction - 1) / reduction;
                component.SampleStride = component.Stride / reduction;
                component.SamplePaddedHeight = component.PaddedHeight / reduction;
                if (Progressive)
                {
                    component.Progression = new(allocator, checked(component.Stride * component.PaddedHeight));
                }
                else if (allocator is not null)
                {
                    component.Samples = allocator.Allocate(checked(component.SampleStride * component.SamplePaddedHeight));
                }
            }
        }

        private void ReadQuantization(ReadOnlySpan<byte> segment)
        {
            if (segment.IsEmpty)
            {
                throw new InvalidDataException("JPEG DQT has no tables.");
            }
            while (!segment.IsEmpty)
            {
                int precision = segment[0] >> 4;
                int index = segment[0] & 15;
                int bytes = 64 * (precision + 1);
                if (precision > 1 || index > 3 || segment.Length < bytes + 1)
                {
                    throw new InvalidDataException("JPEG quantization table precision, index or extent is invalid.");
                }
                int[] table = new int[64];
                for (int i = 0; i < 64; i++)
                {
                    int value = precision == 0 ? segment[1 + i] : BinaryPrimitives.ReadUInt16BigEndian(segment[(1 + i * 2)..]);
                    if (value == 0)
                    {
                        throw new InvalidDataException("JPEG quantization values must be nonzero.");
                    }
                    table[JpegInverseTransform.Zigzag[i]] = value;
                }
                if (lockedQuantization[index] && !table.AsSpan().SequenceEqual(quantization[index]))
                {
                    throw new InvalidDataException("JPEG quantization tables cannot change between progressive scans of a component.");
                }
                quantization[index] = table;
                wideQuantization[index] = precision == 1;
                segment = segment[(bytes + 1)..];
            }
        }

        private void ReadHuffman(ReadOnlySpan<byte> segment)
        {
            if (segment.IsEmpty)
            {
                throw new InvalidDataException("JPEG DHT has no tables.");
            }
            while (!segment.IsEmpty)
            {
                if (segment.Length < 17 || (segment[0] >> 4) > 1 || (segment[0] & 15) > 3)
                {
                    throw new InvalidDataException("JPEG Huffman table selector or header is invalid.");
                }
                int count = 0;
                for (int i = 1; i <= 16; i++)
                {
                    count += segment[i];
                }
                if (count > 256 || segment.Length - 17 < count)
                {
                    throw new InvalidDataException("JPEG Huffman table symbols exceed the segment.");
                }
                int index = (segment[0] >> 4) * 4 + (segment[0] & 15);
                huffman[index] = new(segment.Slice(1, 16), segment.Slice(17, count));
                segment = segment[(17 + count)..];
            }
        }
    }
}
