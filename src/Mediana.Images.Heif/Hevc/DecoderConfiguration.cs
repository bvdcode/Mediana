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
        private DecoderConfiguration(int lengthSize, IReadOnlyList<NalPayload> units)
        {
            LengthSize = lengthSize;
            Units = units;
        }

        public int LengthSize { get; }
        public IReadOnlyList<NalPayload> Units { get; }

        public static DecoderConfiguration Read(ReadOnlyMemory<byte> data)
        {
            ByteReader reader = new ByteReader(data);
            if (reader.U8() != 1)
            {
                throw new UnsupportedHeicFeatureException("HEVC configuration version");
            }
            reader.Skip(20);
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
            return new DecoderConfiguration(lengthSize, units.AsReadOnly());
        }
    }
}
