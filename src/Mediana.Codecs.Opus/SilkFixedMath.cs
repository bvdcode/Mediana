// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkFixedMath
    {
        public static int Exp2Q7(int logarithm)
        {
            if (logarithm < 0) { return 0; }
            if (logarithm >= 3967) { return int.MaxValue; }
            int integral = 1 << (logarithm >> 7), fraction = logarithm & 127;
            int interpolation = fraction + (-174 * fraction * (128 - fraction) >> 16);
            if (logarithm < 2048) { return integral + (integral * interpolation >> 7); }
            return integral + (integral >> 7) * interpolation;
        }

        public static int RoundShift(long value, int bits) => checked((int)(((value >> (bits - 1)) + 1) >> 1));

        public static int Saturate32(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }
}
