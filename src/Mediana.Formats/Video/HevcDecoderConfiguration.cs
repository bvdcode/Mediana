// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Video
{
    /// <summary>HEVCDecoderConfigurationRecord used by HEIF, MP4 and Matroska.</summary>
    public class HevcDecoderConfiguration
    {
        public const string CodecId = "hevc";
        public const int HeaderBytes = 23;

        private HevcDecoderConfiguration(ReadOnlyMemory<byte> data, IReadOnlyList<HevcNalArray> arrays)
        {
            ReadOnlySpan<byte> bytes = data.Span;
            ProfileSpace = bytes[1] >> 6;
            HighTier = (bytes[1] & 32) != 0;
            Profile = bytes[1] & 31;
            ProfileCompatibilityFlags = BinaryPrimitives.ReadUInt32BigEndian(bytes[2..]);
            ConstraintIndicatorFlags = ((ulong)BinaryPrimitives.ReadUInt16BigEndian(bytes[6..]) << 32) |
                BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]);
            Level = bytes[12];
            MinimumSpatialSegmentation = BinaryPrimitives.ReadUInt16BigEndian(bytes[13..]) & 4095;
            ParallelismType = bytes[15] & 3;
            ChromaFormat = bytes[16] & 3;
            LumaBitDepth = (bytes[17] & 7) + 8;
            ChromaBitDepth = (bytes[18] & 7) + 8;
            AverageFrameRate = BinaryPrimitives.ReadUInt16BigEndian(bytes[19..]);
            ConstantFrameRate = bytes[21] >> 6;
            TemporalLayers = (bytes[21] >> 3) & 7;
            TemporalIdNested = (bytes[21] & 4) != 0;
            LengthSize = (bytes[21] & 3) + 1;
            Arrays = arrays;
        }

        public int ProfileSpace { get; }
        public bool HighTier { get; }
        public int Profile { get; }
        public uint ProfileCompatibilityFlags { get; }
        public ulong ConstraintIndicatorFlags { get; }
        public int Level { get; }
        public int MinimumSpatialSegmentation { get; }
        public int ParallelismType { get; }
        public int ChromaFormat { get; }
        public int LumaBitDepth { get; }
        public int ChromaBitDepth { get; }
        public int AverageFrameRate { get; }
        public int ConstantFrameRate { get; }
        public int TemporalLayers { get; }
        public bool TemporalIdNested { get; }
        public int LengthSize { get; }
        public IReadOnlyList<HevcNalArray> Arrays { get; }

        /// <summary>Parses a configuration without copying NAL payloads. The input must remain immutable.</summary>
        public static HevcDecoderConfiguration Read(ReadOnlyMemory<byte> data)
        {
            ReadOnlySpan<byte> bytes = data.Span;
            Require(bytes, 0, HeaderBytes);
            if (bytes[0] != 1)
            {
                throw new NotSupportedException("The HEVC decoder configuration version is not supported.");
            }
            if ((bytes[21] & 3) == 2)
            {
                throw new InvalidDataException("The HEVC NAL length field size is reserved.");
            }
            int offset = HeaderBytes;
            HevcNalArray[] arrays = new HevcNalArray[bytes[22]];
            for (int index = 0; index < arrays.Length; index++)
            {
                Require(bytes, offset, 3);
                int flags = bytes[offset];
                int type = flags & 63;
                if ((flags & 64) != 0)
                {
                    throw new InvalidDataException("A reserved HEVC NAL array bit is nonzero.");
                }
                int count = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 1)..]);
                offset += 3;
                Require(bytes, offset, checked(count * 4));
                ReadOnlyMemory<byte>[] units = new ReadOnlyMemory<byte>[count];
                for (int unitIndex = 0; unitIndex < count; unitIndex++)
                {
                    Require(bytes, offset, 2);
                    int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
                    offset += 2;
                    Require(bytes, offset, length);
                    ReadOnlyMemory<byte> unit = data.Slice(offset, length);
                    if (length < 2 || (unit.Span[0] & 128) != 0 || (unit.Span[1] & 7) == 0 ||
                        ((unit.Span[0] >> 1) & 63) != type)
                    {
                        throw new InvalidDataException("An HEVC configuration NAL header is invalid or disagrees with its array type.");
                    }
                    units[unitIndex] = unit;
                    offset += length;
                }
                arrays[index] = new HevcNalArray(type, (flags & 128) != 0, units);
            }
            if (offset != bytes.Length)
            {
                throw new InvalidDataException("Unexpected trailing HEVC decoder configuration bytes.");
            }
            return new HevcDecoderConfiguration(data, Array.AsReadOnly(arrays));
        }

        private static void Require(ReadOnlySpan<byte> data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
            {
                throw new InvalidDataException("The HEVC decoder configuration is truncated.");
            }
        }
    }
}
