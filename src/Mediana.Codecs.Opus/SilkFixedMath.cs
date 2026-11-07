// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

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

        public static int SaturatingLeftShift(int value, int bits)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bits);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bits, 31);
            return unchecked(Math.Clamp(value, int.MinValue >> bits, int.MaxValue >> bits) << bits);
        }

        public static int InversePositive(int denominator, int precision)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(precision);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, 62);
            int headroom = BitOperations.LeadingZeroCount((uint)denominator) - 1;
            int normalized = denominator << headroom, inverse = (int.MaxValue >> 2) / (normalized >> 16);
            int initial = inverse << 16;
            int error = ((1 << 29) - (int)((long)normalized * (short)inverse >> 16)) << 3;
            int refined = unchecked(initial + (int)((long)error * inverse >> 16));
            return Rescale(refined, 61 - headroom - precision);
        }

        public static int DividePositive(int numerator, int denominator, int precision)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(numerator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
            ArgumentOutOfRangeException.ThrowIfNegative(precision);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, 31);
            if (numerator == 0) { return 0; }
            int numeratorHeadroom = BitOperations.LeadingZeroCount((uint)numerator) - 1;
            int denominatorHeadroom = BitOperations.LeadingZeroCount((uint)denominator) - 1;
            int normalizedNumerator = numerator << numeratorHeadroom, normalizedDenominator = denominator << denominatorHeadroom;
            int inverse = (int.MaxValue >> 2) / (normalizedDenominator >> 16);
            int initial = (int)((long)normalizedNumerator * (short)inverse >> 16);
            int residual = unchecked(normalizedNumerator - ((int)((long)normalizedDenominator * initial >> 32) << 3));
            int refined = unchecked(initial + (int)((long)residual * (short)inverse >> 16));
            return Rescale(refined, 29 + numeratorHeadroom - denominatorHeadroom - precision);
        }

        private static int Rescale(int value, int rightShift)
        {
            if (rightShift >= 32) { return 0; }
            if (rightShift >= 0) { return value >> rightShift; }
            if (rightShift <= -32)
            {
                if (value == 0) { return 0; }
                if (value > 0) { return int.MaxValue; }
                return int.MinValue;
            }
            return SaturatingLeftShift(value, -rightShift);
        }
    }
}
