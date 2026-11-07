// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkExcitation
    {
        public static int Reconstruct(ReadOnlySpan<short> pulses, Span<int> excitationQ14,
            SilkSignalType signal, int quantizationOffset, int seed)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(quantizationOffset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(quantizationOffset, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(seed);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(seed, 3);
            ArgumentOutOfRangeException.ThrowIfLessThan(excitationQ14.Length, pulses.Length, nameof(excitationQ14));
            int offsetQ10 = (signal, quantizationOffset) switch
            {
                (SilkSignalType.Inactive or SilkSignalType.Unvoiced, 0) => 100,
                (SilkSignalType.Inactive or SilkSignalType.Unvoiced, 1) => 240,
                (SilkSignalType.Voiced, 0) => 32,
                (SilkSignalType.Voiced, 1) => 100,
                _ => throw new ArgumentOutOfRangeException(nameof(signal))
            };
            for (int at = 0; at < pulses.Length; at++)
            {
                seed = unchecked(seed * 196314165 + 907633515);
                int value = pulses[at] << 14;
                if (value > 0) { value -= 80 << 4; }
                else if (value < 0) { value += 80 << 4; }
                value += offsetQ10 << 4;
                if (seed < 0) { value = -value; }
                excitationQ14[at] = value;
                seed = unchecked(seed + pulses[at]);
            }
            return seed;
        }
    }
}
