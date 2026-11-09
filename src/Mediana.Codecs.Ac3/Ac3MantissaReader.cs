// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3
{
    internal struct Ac3MantissaReader
    {
        private const long Binary64ExponentBias = 1023;
        private const int Binary64MantissaBits = 52;
        private int threeCode, threeCount, fiveCode, fiveCount, elevenCode, elevenCount;
        internal static double ApplyExponent(double mantissa, int exponent) =>
            mantissa * BitConverter.Int64BitsToDouble((Binary64ExponentBias - exponent) << Binary64MantissaBits);
        public double Read(ref Ac3BitReader bits, int bap)
        {
            switch (bap)
            {
                case 0: return 0;
                case 1: return Group(ref bits, ref threeCode, ref threeCount, 3, 3, 5);
                case 2: return Group(ref bits, ref fiveCode, ref fiveCount, 5, 3, 7);
                case 3: return Symmetric(bits.Read(3), 7);
                case 4: return Group(ref bits, ref elevenCode, ref elevenCount, 11, 2, 7);
                case 5: return Symmetric(bits.Read(4), 15);
                case >= 6 and <= 15:
                    int count = bap switch { <= 13 => bap - 1, 14 => 14, 15 => 16, _ => throw new InvalidOperationException() };
                    int code = bits.Read(count);
                    if ((code & 1 << (count - 1)) != 0) { code -= 1 << count; }
                    return ApplyExponent(code, count - 1);
                default: throw new InvalidDataException("The AC-3 bit allocation pointer is invalid.");
            }
        }
        private static double Group(ref Ac3BitReader bits, ref int code, ref int count, int levels, int length, int bitsPerGroup)
        {
            if (count == 0)
            {
                code = bits.Read(bitsPerGroup); count = length;
                int maximum = length == 3 ? levels * levels * levels : levels * levels;
                if (code >= maximum) { throw new InvalidDataException("The AC-3 grouped mantissa is reserved."); }
            }
            int divisor = count switch { 3 => levels * levels, 2 => levels, 1 => 1, _ => throw new InvalidOperationException() };
            int value = code / divisor; code %= divisor; count--;
            return Symmetric(value, levels);
        }
        private static double Symmetric(int code, int levels)
        {
            if (code >= levels) { throw new InvalidDataException("The AC-3 symmetric mantissa is reserved."); }
            return (2 * code + 1 - levels) / (double)levels;
        }
    }
}
