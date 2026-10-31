// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    /// <summary>Converts complete length-prefixed AVC access units to Annex B with an AUD and IDR parameter sets. Configuration is copied at construction.</summary>
    public class AvcAnnexBConverter
    {
        private readonly int lengthSize;
        private readonly List<AvcParameterSet> parameters = [];
        private readonly int parameterBytes;

        /// <summary>Optional CICP colors replace every configuration SPS and its known in-band occurrences.
        /// Rewriting uses the supplied allocator and a per-SPS byte limit; retained parameter arrays are ordinary managed metadata.</summary>
        public AvcAnnexBConverter(ReadOnlyMemory<byte> configuration, VideoColorDescription? colorDescription = null,
            MediaBufferAllocator? allocator = null, int maximumParameterSetBytes = ushort.MaxValue)
        {
            CicpColorDescription? color = colorDescription switch
            {
                null => null,
                CicpColorDescription cicp => cicp,
                _ => throw new NotSupportedException("The source color description cannot be represented in H.264 SPS parameters.")
            };
            if (color is not null)
            {
                ArgumentNullException.ThrowIfNull(allocator);
                ArgumentOutOfRangeException.ThrowIfLessThan(maximumParameterSetBytes, 8);
                color.Resolve(new VideoColorInfo());
            }
            configuration = configuration.ToArray();
            ReadOnlySpan<byte> data = configuration.Span;
            if (data.Length < 7 || data[0] != 1 || (data[4] & 0xFC) != 0xFC || (data[5] & 0xE0) != 0xE0)
            {
                throw new InvalidDataException("The AVC configuration record is invalid.");
            }
            lengthSize = (data[4] & 3) + 1;
            if (lengthSize == 3)
            {
                throw new InvalidDataException("The AVC NAL length size is reserved.");
            }
            int offset = 6;
            ReadParameters(configuration, ref offset, data[5] & 31, 7, color, allocator, maximumParameterSetBytes);
            if (offset >= data.Length)
            {
                throw new InvalidDataException("The AVC picture parameter count is missing.");
            }
            int pictureCount = data[offset++];
            ReadParameters(configuration, ref offset, pictureCount, 8, color, allocator, maximumParameterSetBytes);
            if (offset < data.Length)
            {
                if (data[1] is not (100 or 110 or 122 or 144) || data.Length - offset < 4 ||
                    (data[offset] & 0xFC) != 0xFC || (data[offset + 1] & 0xF8) != 0xF8 || (data[offset + 2] & 0xF8) != 0xF8)
                {
                    throw new InvalidDataException("The AVC configuration extension is invalid.");
                }
                if (data[offset + 3] != 0)
                {
                    throw new NotSupportedException("AVC sequence parameter extensions are not supported by the converter.");
                }
                offset += 4;
            }
            if (offset != data.Length)
            {
                throw new InvalidDataException("The AVC configuration has unexpected trailing data.");
            }
            parameterBytes = parameters.Sum(parameter => 4 + parameter.Output.Length);
        }

        /// <summary>Returns an owned output buffer; input storage is borrowed only for this call.</summary>
        public MediaBuffer Convert(ReadOnlySpan<byte> data, MediaBufferAllocator allocator, int maximumBytes, out bool idr)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
            idr = false;
            bool nonIdr = false;
            long size = 6;
            int offset = 0;
            while (offset < data.Length)
            {
                ReadOnlySpan<byte> nal = ReadNal(data, ref offset);
                int type = nal[0] & 31;
                int nalBytes = nal.Length;
                if ((nal[0] & 0x80) != 0)
                {
                    throw new InvalidDataException("The AVC forbidden bit is set.");
                }
                switch (type)
                {
                    case 1:
                        nonIdr = true;
                        break;
                    case 5:
                        idr = true;
                        break;
                    case 7 or 8:
                        nalBytes = ParameterOutput(nal).Length;
                        break;
                    case 6 or 9 or 10 or 11 or 12:
                        break;
                    default:
                        throw new NotSupportedException("The AVC NAL type is not supported by the converter.");
                }
                if (type != 9)
                {
                    size += 4L + nalBytes;
                }
            }
            if (idr == nonIdr)
            {
                throw new InvalidDataException("An AVC input packet must contain one AVC access unit with a consistent IDR indication.");
            }
            size += idr ? parameterBytes : 0;
            if (size > maximumBytes)
            {
                throw new NotSupportedException("The Annex B access unit exceeds the configured packet limit.");
            }
            using WritableMediaBuffer result = allocator.Allocate((int)size);
            Span<byte> output = result.Memory.Span;
            new byte[] { 0, 0, 0, 1, 9, 0xF0 }.CopyTo(output);
            int written = 6;
            if (idr)
            {
                foreach (AvcParameterSet parameter in parameters)
                {
                    Append(output, ref written, parameter.Output.Span);
                }
            }
            offset = 0;
            while (offset < data.Length)
            {
                ReadOnlySpan<byte> nal = ReadNal(data, ref offset);
                int type = nal[0] & 31;
                if (type != 9)
                {
                    Append(output, ref written, type is 7 or 8 ? ParameterOutput(nal) : nal);
                }
            }
            return result.Publish();
        }

        private void ReadParameters(ReadOnlyMemory<byte> data, ref int offset, int count, int type,
            CicpColorDescription? color, MediaBufferAllocator? allocator, int maximumBytes)
        {
            if (count == 0)
            {
                throw new NotSupportedException("Annex B output requires out-of-band AVC sequence and picture parameters.");
            }
            for (int index = 0; index < count; index++)
            {
                if (data.Length - offset < 2)
                {
                    throw new InvalidDataException("The AVC parameter length is truncated.");
                }
                int length = BinaryPrimitives.ReadUInt16BigEndian(data.Span[offset..]);
                offset += 2;
                if (length == 0 || length > data.Length - offset || (data.Span[offset] & 0x9F) != type)
                {
                    throw new InvalidDataException("The AVC parameter NAL is invalid.");
                }
                ReadOnlyMemory<byte> original = data.Slice(offset, length);
                ReadOnlyMemory<byte> output = original;
                if (type == 7 && color is not null)
                {
                    using MediaBuffer rewritten = H264ColorDescriptionWriter.Rewrite(original.Span, color, allocator!, maximumBytes);
                    output = rewritten.Memory.ToArray();
                }
                parameters.Add(new(original, output));
                offset += length;
            }
        }

        private ReadOnlySpan<byte> ParameterOutput(ReadOnlySpan<byte> nal)
        {
            foreach (AvcParameterSet parameter in parameters)
            {
                if (nal.SequenceEqual(parameter.Original.Span))
                {
                    return parameter.Output.Span;
                }
            }
            throw new NotSupportedException("Changing AVC parameter sets requires a new converter.");
        }

        private ReadOnlySpan<byte> ReadNal(ReadOnlySpan<byte> data, ref int offset)
        {
            if (data.Length - offset < lengthSize)
            {
                throw new InvalidDataException("The AVC NAL length is truncated.");
            }
            uint length = 0;
            for (int index = 0; index < lengthSize; index++)
            {
                length = length << 8 | data[offset++];
            }
            if (length == 0 || length > data.Length - offset)
            {
                throw new InvalidDataException("The AVC NAL payload is invalid.");
            }
            ReadOnlySpan<byte> nal = data.Slice(offset, (int)length);
            offset += (int)length;
            return nal;
        }

        private static void Append(Span<byte> output, ref int offset, ReadOnlySpan<byte> nal)
        {
            BinaryPrimitives.WriteUInt32BigEndian(output[offset..], 1);
            offset += 4;
            nal.CopyTo(output[offset..]);
            offset += nal.Length;
        }
    }
}
