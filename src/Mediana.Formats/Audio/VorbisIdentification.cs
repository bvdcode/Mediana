// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Audio
{
    /// <summary>Vorbis I identification declarations. Nonpositive bitrates mean unspecified.</summary>
    public class VorbisIdentification
    {
        public const string CodecId = "vorbis";
        public const int HeaderBytes = 30;

        private VorbisIdentification(ReadOnlySpan<byte> data)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data[7..]) != 0)
            {
                throw new NotSupportedException("The Vorbis bitstream version is not supported.");
            }
            ChannelCount = data[11];
            uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            if (ChannelCount == 0 || sampleRate == 0 || sampleRate > int.MaxValue)
            {
                throw new InvalidDataException("The Vorbis channel count or sample rate is invalid.");
            }
            SampleRate = (int)sampleRate;
            MaximumBitRate = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
            NominalBitRate = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);
            MinimumBitRate = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
            int small = data[28] & 15, large = data[28] >> 4;
            if (small < 6 || large > 13 || large < small || (data[29] & 1) == 0)
            {
                throw new InvalidDataException("The Vorbis block sizes or identification framing bit are invalid.");
            }
            SmallBlockSize = 1 << small;
            LargeBlockSize = 1 << large;
        }

        public int ChannelCount { get; }
        public int SampleRate { get; }
        public int MaximumBitRate { get; }
        public int NominalBitRate { get; }
        public int MinimumBitRate { get; }
        public int SmallBlockSize { get; }
        public int LargeBlockSize { get; }

        public static VorbisIdentification Read(ReadOnlySpan<byte> data)
        {
            if (data.Length != HeaderBytes || data[0] != 1 || !data.Slice(1, 6).SequenceEqual("vorbis"u8))
            {
                throw new InvalidDataException("The Vorbis identification header is absent, truncated or has an invalid length.");
            }
            return new(data);
        }
    }
}
