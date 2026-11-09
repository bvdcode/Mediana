// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal record AacPsHeader(int? IntensityMode, int? CoherenceMode, bool ExtensionEnabled)
    {
        public int StereoBands => IntensityMode is 2 or 5 || CoherenceMode is 2 or 5 ? 34 : 20;

        public static AacPsHeader Read(ref AacBitReader bits)
        {
            int? intensity = ReadMode(ref bits);
            int? coherence = ReadMode(ref bits);
            return new AacPsHeader(intensity, coherence, bits.Read(1) != 0);
        }

        public static int ParameterCount(int mode) => mode switch
        {
            0 or 3 => 10,
            1 or 4 => 20,
            2 or 5 => 34,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        public static int PhaseCount(int mode) => mode switch
        {
            0 or 3 => 5,
            1 or 4 => 11,
            2 or 5 => 17,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        private static int? ReadMode(ref AacBitReader bits)
        {
            if (bits.Read(1) == 0)
            {
                return null;
            }
            int mode = bits.Read(3);
            if (mode > 5)
            {
                throw new InvalidDataException("The PS parameter mode is reserved.");
            }
            return mode;
        }
    }
}
