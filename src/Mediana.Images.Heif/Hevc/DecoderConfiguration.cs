// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using Mediana.Formats.Video;

using Mediana.Codecs.Hevc;

namespace Mediana.Images.Heif.Hevc
{
    internal class DecoderConfiguration
    {
        private const int MaxConfigurationUnits = 256;
        private DecoderConfiguration(HevcDecoderConfiguration configuration, IReadOnlyList<NalPayload> units)
        {
            LengthSize = configuration.LengthSize;
            Units = units;
            Profile = configuration.Profile;
            ProfileSpace = configuration.ProfileSpace;
            Level = configuration.Level;
            HighTier = configuration.HighTier;
            ChromaFormat = configuration.ChromaFormat;
            LumaBitDepth = configuration.LumaBitDepth;
            ChromaBitDepth = configuration.ChromaBitDepth;
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
            HevcDecoderConfiguration configuration = HevcDecoderConfiguration.Read(data);
            List<NalPayload> units = new List<NalPayload>();
            foreach (HevcNalArray array in configuration.Arrays)
            {
                foreach (ReadOnlyMemory<byte> bytes in array.Units)
                {
                    if (units.Count >= MaxConfigurationUnits)
                    {
                        throw new InvalidDataException("HEVC configuration parameter set limit exceeded.");
                    }
                    units.Add(new NalPayload(bytes));
                }
            }
            return new DecoderConfiguration(configuration, units.AsReadOnly());
        }
    }
}
