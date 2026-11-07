// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Opus
{
    internal static class SilkSignalMath
    {
        public static (int Value, int Shift) Energy(ReadOnlySpan<short> samples)
        {
            if (samples.IsEmpty) { return (0, 0); }
            int shift = BitOperations.Log2((uint)samples.Length);
            uint estimate = (uint)samples.Length + SumSquares(samples, shift);
            shift = Math.Max(0, shift + 3 - BitOperations.LeadingZeroCount(estimate));
            return ((int)SumSquares(samples, shift), shift);
        }

        private static uint SumSquares(ReadOnlySpan<short> samples, int shift)
        {
            uint sum = 0;
            int at = 0;
            for (; at + 1 < samples.Length; at += 2)
            {
                uint pair = (uint)(samples[at] * samples[at] + samples[at + 1] * samples[at + 1]);
                sum += pair >> shift;
            }
            if (at < samples.Length) { sum += (uint)(samples[at] * samples[at]) >> shift; }
            return sum;
        }

        public static int SquareRoot(int value)
        {
            if (value <= 0) { return 0; }
            int zeros = BitOperations.LeadingZeroCount((uint)value);
            int fraction = (int)BitOperations.RotateRight((uint)value, 24 - zeros) & 127;
            int root = (zeros & 1) switch { 0 => 46214, 1 => 32768, _ => throw new InvalidOperationException() };
            root >>= zeros / 2;
            return root + (root * (short)(213 * fraction) >> 16);
        }

        public static void Expand(Span<short> coefficients, int chirpQ16)
        {
            int delta = chirpQ16 - 65536;
            for (int at = 0; at < coefficients.Length; at++)
            {
                coefficients[at] = (short)SilkFixedMath.RoundShift(unchecked(chirpQ16 * coefficients[at]), 16);
                chirpQ16 += SilkFixedMath.RoundShift(unchecked(chirpQ16 * delta), 16);
            }
        }
    }
}
