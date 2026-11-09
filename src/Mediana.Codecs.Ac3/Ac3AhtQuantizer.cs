// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3
{
    /// <summary>ETSI TS 102 366 E.2.4.4 symmetric and gain-adaptive inverse quantization.</summary>
    internal static class Ac3AhtQuantizer
    {
        private static ReadOnlySpan<short> UnityRemap => [4681, 2185, 1057, 520, 258, 129, 64, 32, 16, 8, 2, 0];
        private static ReadOnlySpan<short> DoubleRemap => [-10923, -14043, -15292, -15855, -16124, -16255, -16320, -16352, -16368];
        private static ReadOnlySpan<short> QuadrupleRemap => [-4681, -6554, -7399, -7802, -7998, -8096, -8144, -8168, -8180];
        private static ReadOnlySpan<short> DoubleNegativeBias => [-5461, -11703, -14199, -15327, -15864, -16126, -16255, -16320, -16352];
        private static ReadOnlySpan<short> QuadrupleNegativeBias => [-1170, -4915, -6606, -7412, -7805, -7999, -8096, -8144, -8168];

        public static int Bits(int allocation) => allocation switch
        {
            0 => 0,
            1 => 2,
            2 => 3,
            3 => 4,
            4 => 5,
            5 => 7,
            6 => 8,
            7 => 9,
            >= 8 and <= 17 => allocation - 5,
            18 => 14,
            19 => 16,
            _ => throw new InvalidDataException("The E-AC-3 high-efficiency allocation pointer is invalid.")
        };

        public static double Read(ref Ac3BitReader bits, int allocation, int gain)
        {
            int width = Bits(allocation), smallWidth = width - gain;
            int code = Signed(ref bits, smallWidth);
            if (gain == 0)
            {
                if (code == -(1 << (width - 1)))
                { throw new InvalidDataException("The E-AC-3 symmetric adaptive mantissa is reserved."); }
                double value = Ac3MantissaReader.ApplyExponent(code, width - 1);
                return value + value * UnityRemap[allocation - 8] / 32768.0;
            }
            if (code != -(1 << (smallWidth - 1)))
            { return Ac3MantissaReader.ApplyExponent(code, width - 1); }
            int largeWidth = gain switch
            {
                1 => width - 1,
                2 => width,
                _ => throw new InvalidDataException("The E-AC-3 adaptive mantissa gain is invalid.")
            };
            code = Signed(ref bits, largeWidth);
            double large = Ac3MantissaReader.ApplyExponent(code, largeWidth - 1);
            int index = allocation - 8;
            short coefficient = gain == 1 ? DoubleRemap[index] : QuadrupleRemap[index];
            double bias = code >= 0 ? (gain == 1 ? .5 : .25) :
                (gain == 1 ? DoubleNegativeBias[index] : QuadrupleNegativeBias[index]) / 32768.0;
            return large + large * coefficient / 32768.0 + bias;
        }

        private static int Signed(ref Ac3BitReader bits, int count)
        {
            int code = bits.Read(count);
            return (code & 1 << (count - 1)) != 0 ? code - (1 << count) : code;
        }
    }
}
