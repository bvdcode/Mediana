// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkLpcAnalysis
    {
        public static void Apply(ReadOnlySpan<short> input, ReadOnlySpan<short> coefficients, Span<short> output)
        {
            int order = coefficients.Length;
            ArgumentOutOfRangeException.ThrowIfLessThan(input.Length, order, nameof(input));
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, input.Length, nameof(output));
            output[..order].Clear();
            for (int at = order; at < input.Length; at++)
            {
                int prediction = 0;
                for (int tap = 0; tap < order; tap++) { prediction = unchecked(prediction + input[at - tap - 1] * coefficients[tap]); }
                int residual = unchecked((input[at] << 12) - prediction);
                output[at] = (short)Math.Clamp(SilkFixedMath.RoundShift(residual, 12), short.MinValue, short.MaxValue);
            }
        }
    }
}
