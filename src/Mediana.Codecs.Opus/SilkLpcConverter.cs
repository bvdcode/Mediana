// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkLpcConverter
    {
        private const int MaximumStabilizationPasses = 16;

        public static void Convert(ReadOnlySpan<short> frequenciesQ15, Span<short> coefficientsQ12)
        {
            int order = frequenciesQ15.Length;
            ReadOnlySpan<byte> ordering = order switch
            {
                10 => [0, 9, 6, 3, 4, 5, 8, 1, 2, 7],
                16 => [0, 15, 8, 7, 4, 11, 12, 3, 2, 13, 10, 5, 6, 9, 14, 1],
                _ => throw new ArgumentException("SILK requires ten or sixteen LSFs.", nameof(frequenciesQ15))
            };
            ArgumentOutOfRangeException.ThrowIfLessThan(coefficientsQ12.Length, order, nameof(coefficientsQ12));
            foreach (short frequency in frequenciesQ15) { ArgumentOutOfRangeException.ThrowIfNegative(frequency, nameof(frequenciesQ15)); }
            Span<int> cosines = stackalloc int[16], even = stackalloc int[9], odd = stackalloc int[9], coefficients = stackalloc int[16];
            for (int at = 0; at < order; at++)
            {
                int position = frequenciesQ15[at] >> 8, fraction = frequenciesQ15[at] & 255;
                int start = CosineQ12[position], delta = CosineQ12[position + 1] - start;
                cosines[ordering[at]] = SilkFixedMath.RoundShift((start << 8) + delta * fraction, 4);
            }
            Polynomial(cosines[..order], even); Polynomial(cosines.Slice(1, order - 1), odd);
            for (int at = 0; at < order / 2; at++)
            {
                int sum = even[at + 1] + even[at], difference = odd[at + 1] - odd[at];
                coefficients[at] = -difference - sum; coefficients[order - at - 1] = difference - sum;
            }
            Span<short> output = coefficientsQ12[..order];
            Fit(coefficients[..order], output);
            for (int pass = 0; pass < MaximumStabilizationPasses && SilkLpcStability.InverseGainQ30(output) == 0; pass++)
            {
                Expand(coefficients[..order], 65536 - (2 << pass));
                for (int at = 0; at < order; at++) { output[at] = (short)SilkFixedMath.RoundShift(coefficients[at], 5); }
            }
        }

        private static void Polynomial(ReadOnlySpan<int> cosines, Span<int> result)
        {
            int degree = (cosines.Length + 1) / 2;
            result[0] = 1 << 16; result[1] = -cosines[0];
            for (int term = 1; term < degree; term++)
            {
                int factor = cosines[2 * term];
                result[term + 1] = (result[term - 1] << 1) - SilkFixedMath.RoundShift((long)factor * result[term], 16);
                for (int at = term; at > 1; at--)
                {
                    result[at] += result[at - 2] - SilkFixedMath.RoundShift((long)factor * result[at - 1], 16);
                }
                result[1] -= factor;
            }
        }

        private static void Fit(Span<int> coefficients, Span<short> output)
        {
            int pass = 0;
            for (; pass < 10; pass++)
            {
                int peak = 0, index = 0;
                for (int at = 0; at < coefficients.Length; at++)
                {
                    int absolute = Math.Abs(coefficients[at]);
                    if (absolute > peak) { peak = absolute; index = at; }
                }
                peak = SilkFixedMath.RoundShift(peak, 5);
                if (peak <= short.MaxValue) { break; }
                peak = Math.Min(peak, 163838);
                int chirp = 65470 - ((peak - short.MaxValue) << 14) / (peak * (index + 1) >> 2);
                Expand(coefficients, chirp);
            }
            for (int at = 0; at < coefficients.Length; at++)
            {
                int coefficient = SilkFixedMath.RoundShift(coefficients[at], 5);
                if (pass == 10)
                {
                    coefficient = Math.Clamp(coefficient, short.MinValue, short.MaxValue);
                    coefficients[at] = coefficient << 5;
                }
                output[at] = (short)coefficient;
            }
        }

        private static void Expand(Span<int> coefficients, int chirp)
        {
            int delta = chirp - 65536;
            for (int at = 0; at < coefficients.Length; at++)
            {
                coefficients[at] = (int)((long)chirp * coefficients[at] >> 16);
                if (at + 1 < coefficients.Length) { chirp += SilkFixedMath.RoundShift(unchecked(chirp * delta), 16); }
            }
        }

        private static readonly short[] CosineQ12 =
        [
            8192, 8190, 8182, 8170, 8152, 8130, 8104, 8072, 8034, 7994, 7946, 7896, 7840, 7778, 7714, 7644,
            7568, 7490, 7406, 7318, 7226, 7128, 7026, 6922, 6812, 6698, 6580, 6458, 6332, 6204, 6070, 5934,
            5792, 5648, 5502, 5352, 5198, 5040, 4880, 4718, 4552, 4382, 4212, 4038, 3862, 3684, 3502, 3320,
            3136, 2948, 2760, 2570, 2378, 2186, 1990, 1794, 1598, 1400, 1202, 1002, 802, 602, 402, 202,
            0, -202, -402, -602, -802, -1002, -1202, -1400, -1598, -1794, -1990, -2186, -2378, -2570, -2760, -2948,
            -3136, -3320, -3502, -3684, -3862, -4038, -4212, -4382, -4552, -4718, -4880, -5040, -5198, -5352, -5502, -5648,
            -5792, -5934, -6070, -6204, -6332, -6458, -6580, -6698, -6812, -6922, -7026, -7128, -7226, -7318, -7406, -7490,
            -7568, -7644, -7714, -7778, -7840, -7896, -7946, -7994, -8034, -8072, -8104, -8130, -8152, -8170, -8182, -8190,
            -8192
        ];
    }
}
