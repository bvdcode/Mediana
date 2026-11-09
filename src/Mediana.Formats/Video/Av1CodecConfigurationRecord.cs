// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>AV1CodecConfigurationRecord header and OBU framing, independent of decoder coverage.</summary>
    public readonly record struct Av1CodecConfigurationRecord
    {
        public const string CodecId = "av1";
        public const int HeaderBytes = 4;

        private Av1CodecConfigurationRecord(ReadOnlySpan<byte> encoded)
        {
            Profile = encoded[1] >> 5;
            Level = encoded[1] & 31;
            HighTier = (encoded[2] & 128) != 0;
            BitDepth = (encoded[2] & 32) != 0 ? 12 : (encoded[2] & 64) != 0 ? 10 : 8;
            Monochrome = (encoded[2] & 16) != 0;
            SubsamplingX = (encoded[2] & 8) != 0;
            SubsamplingY = (encoded[2] & 4) != 0;
            ChromaPosition = encoded[2] & 3;
            InitialPresentationDelay = (encoded[3] & 16) != 0 ? (encoded[3] & 15) + 1 : null;
        }

        public int Profile { get; }
        public int Level { get; }
        public bool HighTier { get; }
        public int BitDepth { get; }
        public bool Monochrome { get; }
        public bool SubsamplingX { get; }
        public bool SubsamplingY { get; }
        public int ChromaPosition { get; }
        public int? InitialPresentationDelay { get; }

        public static Av1CodecConfigurationRecord Read(ReadOnlySpan<byte> encoded)
        {
            if (encoded.Length < HeaderBytes || encoded[0] != 0x81 || (encoded[3] & 0xE0) != 0 ||
                ((encoded[3] & 16) == 0 && (encoded[3] & 15) != 0))
            {
                throw new InvalidDataException("The AV1 codec configuration record is invalid.");
            }
            Av1CodecConfigurationRecord record = new(encoded);
            bool highBitDepth = (encoded[2] & 64) != 0;
            if (record.Profile > 2 || (record.Level is >= 24 and <= 30) ||
                (record.HighTier && record.Level <= 7) ||
                (record.BitDepth == 12 && (!highBitDepth || record.Profile != 2)) ||
                record.ChromaPosition == 3 || (!record.SubsamplingX && record.SubsamplingY) ||
                ((!record.SubsamplingX || !record.SubsamplingY || record.Monochrome) && record.ChromaPosition != 0) ||
                (record.Monochrome && (!record.SubsamplingX || !record.SubsamplingY)) ||
                (record.Profile == 0 && (!record.SubsamplingX || !record.SubsamplingY)) ||
                (record.Profile == 1 && (record.Monochrome || record.SubsamplingX || record.SubsamplingY)) ||
                (record.Profile == 2 && record.BitDepth < 12 && !record.Monochrome && (!record.SubsamplingX || record.SubsamplingY)))
            {
                throw new InvalidDataException("The AV1 codec configuration profile, depth, level or chroma signal is invalid.");
            }
            Av1ObuReader obus = new(encoded[HeaderBytes..]);
            bool first = true;
            while (obus.TryRead(out int type, out _))
            {
                if (type == 1 && !first)
                {
                    throw new InvalidDataException("The AV1 configuration sequence header must be its first and only sequence OBU.");
                }
                first = false;
            }
            return record;
        }
    }
}
