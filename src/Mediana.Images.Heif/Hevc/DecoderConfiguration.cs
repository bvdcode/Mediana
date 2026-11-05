// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using Mediana.Images.Heif.Binary;

namespace Mediana.Images.Heif.Hevc
{
    internal class DecoderConfiguration
    {
        private const int MaxConfigurationUnits = 256;
        private DecoderConfiguration(int lengthSize, IReadOnlyList<NalPayload> units, int profile, int profileSpace,
            int level, bool highTier, int chromaFormat, int lumaBitDepth, int chromaBitDepth)
        {
            LengthSize = lengthSize;
            Units = units;
            Profile = profile;
            ProfileSpace = profileSpace;
            Level = level;
            HighTier = highTier;
            ChromaFormat = chromaFormat;
            LumaBitDepth = lumaBitDepth;
            ChromaBitDepth = chromaBitDepth;
        }

        public int LengthSize { get; }
        public IReadOnlyList<NalPayload> Units { get; }
        public int Profile { get; }
        public int ProfileSpace { get; }
        public int Level { get; }
        public bool HighTier { get; }
        public int ChromaFormat { get; }
        public int LumaBitDepth { get; }
        public int ChromaBitDepth { get; }

        public static DecoderConfiguration Read(ReadOnlyMemory<byte> data)
        {
            ByteReader reader = new ByteReader(data);
            if (reader.U8() != 1)
            {
                throw new UnsupportedHeicFeatureException("HEVC configuration version");
            }
            int profile = reader.U8();
            reader.Skip(10);
            int level = reader.U8();
            reader.Skip(3);
            int chromaFormat = reader.U8() & 3;
            int lumaBitDepth = 8 + (reader.U8() & 7);
            int chromaBitDepth = 8 + (reader.U8() & 7);
            reader.Skip(2);
            int lengthSize = (reader.U8() & 3) + 1;
            if (lengthSize == 3)
            {
                throw new InvalidDataException("Reserved HEVC NAL length field size.");
            }
            int arrays = reader.U8();
            List<NalPayload> units = new List<NalPayload>();
            for (int array = 0; array < arrays; array++)
            {
                int type = reader.U8() & 63;
                int count = reader.U16();
                for (int i = 0; i < count; i++)
                {
                    NalPayload unit = new NalPayload(reader.Bytes(reader.U16()));
                    if (unit.Type != type)
                    {
                        throw new InvalidDataException("HEVC configuration NAL type mismatch.");
                    }
                    if (units.Count >= MaxConfigurationUnits)
                    {
                        throw new InvalidDataException("HEVC configuration parameter set limit exceeded.");
                    }
                    units.Add(unit);
                }
            }

            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Unexpected HEVC configuration data.");
            }
            return new DecoderConfiguration(lengthSize, units.AsReadOnly(), profile & 31, profile >> 6,
                level, (profile & 32) != 0, chromaFormat, lumaBitDepth, chromaBitDepth);
        }
    }
}
