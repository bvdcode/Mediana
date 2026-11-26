// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.ProRes
{
    internal static class ProResPictureDecoder
    {
        internal static VideoPixelFormat GetPixelFormat(ProResFrameHeader header) =>
            new PlanarVideoPixelFormat($"yuv{(header.ChromaFormat == 2 ? "422" : "444")}p{header.BitDepth}",
                PlanarColorModel.Yuv, header.BitDepth, header.ChromaFormat == 2 ? 2 : 1, 1, header.AlphaType == 0 ? 0 : 16);

        public static VideoFrame Decode(MediaPacket packet, EncodedStreamInfo stream, MediaBufferAllocator allocator, ProResDecoderOptions options, Action<string>? warningHandler, CancellationToken token)
        {
            ProResFrameHeader header = ProResFrameHeader.Read(packet.Data);
            if (header.FrameBytes != packet.Data.Length)
            {
                throw new InvalidDataException("The ProRes frame size disagrees with its access unit.");
            }
            int depth = header.BitDepth;
            SampleAspectRatio? aspect = header.SampleAspectRatio;
            VideoFormat format = new(header.Width, header.Height, GetPixelFormat(header), header.Color,
                stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription, header.ScanMode);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            int position = header.HeaderBytes;
            bool recoveredAlpha = false;
            int pictures = header.InterlaceMode == 0 ? 1 : 2;
            using WritableMediaBuffer scratch = allocator.Allocate(32 * 64 * sizeof(int));
            for (int picture = 0; picture < pictures; picture++)
            {
                int parity = header.InterlaceMode switch
                {
                    0 => 0,
                    1 => picture,
                    2 => 1 - picture,
                    _ => throw new InvalidOperationException()
                };
                int rows = pictures == 1 ? header.Height : (header.Height + 1 - parity) / 2;
                position += DecodePicture(packet.Data.Span[position..], header, writable.Memory.Span, planes,
                    scratch.Memory.Span, depth, rows, parity, pictures, options.RecoverLegacyAlpha, ref recoveredAlpha, token);
            }
            foreach (byte value in packet.Data.Span[position..])
            {
                if (value != 0)
                {
                    throw new InvalidDataException("The ProRes frame stuffing is nonzero.");
                }
            }
            if (recoveredAlpha)
            {
                warningHandler?.Invoke("Recovered legacy ProRes alpha termination or padded rows; visible alpha samples were preserved.");
            }
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, planes, packet.PresentationTime, packet.Duration);
        }

        private static int DecodePicture(ReadOnlySpan<byte> encoded, ProResFrameHeader header, Span<byte> output,
            PlaneLayout[] planes, Span<byte> scratch, int depth, int rows, int parity, int rowStep, bool allowLegacyAlpha, ref bool recoveredAlpha, CancellationToken token)
        {
            if (encoded.Length < 8)
            {
                throw new InvalidDataException("The ProRes picture header is truncated.");
            }
            int headerSize = encoded[0] >> 3;
            uint size = BinaryPrimitives.ReadUInt32BigEndian(encoded[1..]);
            if (size > encoded.Length || headerSize < 8 || headerSize > size)
            {
                throw new InvalidDataException("The ProRes picture extent is invalid.");
            }
            encoded = encoded[..(int)size];
            int sliceWidth = encoded[7] >> 4;
            if (sliceWidth > 3)
            {
                throw new InvalidDataException("The ProRes slice width is reserved.");
            }
            int desired = 1 << sliceWidth;
            int columns = (header.Width + 15) / 16;
            int sliceCount = (columns / desired + System.Numerics.BitOperations.PopCount((uint)(columns % desired))) * ((rows + 15) / 16);
            if (BinaryPrimitives.ReadUInt16BigEndian(encoded[5..]) != sliceCount)
            {
                throw new InvalidDataException("The ProRes slice count disagrees with its picture geometry.");
            }
            int table = headerSize;
            int position = checked(table + sliceCount * 2);
            if (position > encoded.Length)
            {
                throw new InvalidDataException("The ProRes slice table is truncated.");
            }
            for (int y = 0; y < rows; y += 16)
            {
                for (int x = 0; x < columns;)
                {
                    token.ThrowIfCancellationRequested();
                    int macroblocks = desired;
                    while (macroblocks > columns - x)
                    {
                        macroblocks >>= 1;
                    }
                    int length = BinaryPrimitives.ReadUInt16BigEndian(encoded[table..]);
                    if (length > encoded.Length - position)
                    {
                        throw new InvalidDataException("The ProRes slice exceeds the picture.");
                    }
                    DecodeSlice(encoded.Slice(position, length), header, output, planes, scratch, depth,
                        x * 16, y, macroblocks, rows, parity, rowStep, allowLegacyAlpha, ref recoveredAlpha);
                    table += 2;
                    position += length;
                    x += macroblocks;
                }
            }
            if (position != encoded.Length)
            {
                throw new InvalidDataException("The ProRes picture size disagrees with its slices.");
            }
            return position;
        }

        private static void DecodeSlice(ReadOnlySpan<byte> encoded, ProResFrameHeader header, Span<byte> output,
            PlaneLayout[] planes, Span<byte> scratch, int depth, int x, int y, int macroblocks, int rows, int parity, int rowStep, bool allowLegacyAlpha, ref bool recoveredAlpha)
        {
            if (encoded.Length < 6)
            {
                throw new InvalidDataException("The ProRes slice header is truncated.");
            }
            int offset = encoded[0] >> 3;
            int quantizer = encoded[1];
            if (offset < (header.AlphaType == 0 ? 6 : 8) || offset > encoded.Length || quantizer is < 1 or > 224)
            {
                throw new InvalidDataException("The ProRes slice header or quantization index is invalid.");
            }
            int scale = quantizer <= 128 ? quantizer : 128 + 4 * (quantizer - 128);
            Span<int> lengths = stackalloc int[3];
            lengths[0] = BinaryPrimitives.ReadUInt16BigEndian(encoded[2..]);
            lengths[1] = BinaryPrimitives.ReadUInt16BigEndian(encoded[4..]);
            lengths[2] = header.AlphaType == 0 ? encoded.Length - offset - lengths[0] - lengths[1] : BinaryPrimitives.ReadUInt16BigEndian(encoded[6..]);
            Span<ushort> blockPixels = stackalloc ushort[64];
            for (int plane = 0; plane < 3; plane++)
            {
                if (lengths[plane] < 1 || lengths[plane] > encoded.Length - offset)
                {
                    throw new InvalidDataException("The ProRes component data extent is invalid.");
                }
                int horizontal = plane != 0 && header.ChromaFormat == 2 ? 2 : 1;
                int blocksPerMb = 4 / horizontal;
                int blocks = blocksPerMb * macroblocks;
                Span<int> coefficients = MemoryMarshal.Cast<byte, int>(scratch)[..(blocks * 64)];
                ProResCoefficients.Read(encoded.Slice(offset, lengths[plane]), coefficients, blocks);
                ReadOnlySpan<byte> weights = plane == 0 ? header.LumaWeights.Span : header.ChromaWeights.Span;
                PlaneLayout layout = planes[plane];
                for (int block = 0; block < blocks; block++)
                {
                    ProResInverseTransform.Apply(coefficients, block, blocks, weights, scale, rowStep == 2, depth, blockPixels);
                    int within = block % blocksPerMb;
                    int blockX = x / horizontal + block / blocksPerMb * (16 / horizontal) + (horizontal == 1 ? (plane == 0 ? within % 2 : within / 2) * 8 : 0);
                    int blockY = y + (horizontal == 1 ? (plane == 0 ? within / 2 : within % 2) : within) * 8;
                    int width = Math.Min(8, layout.RowBytes / 2 - blockX);
                    for (int dy = 0; dy < 8 && blockY + dy < rows && width > 0; dy++)
                    {
                        int target = layout.Offset + ((blockY + dy) * rowStep + parity) * layout.Stride + blockX * 2;
                        MemoryMarshal.AsBytes(blockPixels.Slice(dy * 8, width)).CopyTo(output[target..]);
                    }
                }
                offset += lengths[plane];
            }
            if (header.AlphaType != 0)
            {
                recoveredAlpha |= ProResAlpha.Decode(encoded[offset..], header.AlphaType, output, planes[3], x, y,
                    macroblocks * 16, Math.Min(16, rows - y), parity, rowStep, allowLegacyAlpha);
            }
        }
    }
}
