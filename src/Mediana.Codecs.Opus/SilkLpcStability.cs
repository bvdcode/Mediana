// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Opus
{
    internal static class SilkLpcStability
    {
        private const int ReflectionLimitQ24 = 16773022;
        private const int MinimumInverseGainQ30 = 107374;

        public static int InverseGainQ30(ReadOnlySpan<short> coefficientsQ12)
        {
            if (coefficientsQ12.Length is not (10 or 16)) { throw new ArgumentException("SILK requires ten or sixteen LPC coefficients.", nameof(coefficientsQ12)); }
            Span<int> polynomial = stackalloc int[16];
            int dc = 0;
            for (int at = 0; at < coefficientsQ12.Length; at++)
            {
                dc += coefficientsQ12[at]; polynomial[at] = coefficientsQ12[at] << 12;
            }
            if (dc >= 4096) { return 0; }
            int inverseGain = 1 << 30;
            for (int order = coefficientsQ12.Length - 1; order >= 0; order--)
            {
                int last = polynomial[order];
                if (last is > ReflectionLimitQ24 or < -ReflectionLimitQ24) { return 0; }
                int reflectionQ31 = -(last << 7);
                int scaleQ30 = (1 << 30) - (int)((long)reflectionQ31 * reflectionQ31 >> 32);
                inverseGain = (int)((long)inverseGain * scaleQ30 >> 32) << 2;
                if (inverseGain < MinimumInverseGainQ30) { return 0; }
                if (order == 0) { break; }
                int precision = 32 - BitOperations.LeadingZeroCount((uint)scaleQ30);
                int reciprocal = SilkFixedMath.InversePositive(scaleQ30, precision + 30);
                for (int at = 0; at < (order + 1) / 2; at++)
                {
                    int opposite = order - at - 1, left = polynomial[at], right = polynomial[opposite];
                    int leftResidual = SilkFixedMath.Saturate32((long)left - SilkFixedMath.RoundShift((long)right * reflectionQ31, 31));
                    int rightResidual = SilkFixedMath.Saturate32((long)right - SilkFixedMath.RoundShift((long)left * reflectionQ31, 31));
                    long newLeft = (((long)leftResidual * reciprocal >> (precision - 1)) + 1) >> 1;
                    long newRight = (((long)rightResidual * reciprocal >> (precision - 1)) + 1) >> 1;
                    if (newLeft is > int.MaxValue or < int.MinValue || newRight is > int.MaxValue or < int.MinValue) { return 0; }
                    polynomial[at] = (int)newLeft; polynomial[opposite] = (int)newRight;
                }
            }
            return inverseGain;
        }

    }
}
