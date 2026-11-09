// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1Cdf
    {
        internal const int Top = 1 << 15;
        internal const int MaximumSymbols = 16;

        internal static void Validate(ReadOnlySpan<ushort> cdf)
        {
            int symbols = cdf.Length - 1;
            if (symbols is < 2 or > MaximumSymbols || cdf[symbols - 1] != Top || cdf[symbols] > 32)
            { throw new ArgumentException("An AV1 CDF requires 2 through 16 symbols, a Q15 total and a counter from zero through 32.", nameof(cdf)); }
            int previous = 0;
            for (int index = 0; index < symbols; index++)
            {
                if (cdf[index] < previous || cdf[index] > Top)
                { throw new ArgumentException("An AV1 cumulative probability must not decrease or exceed its Q15 total.", nameof(cdf)); }
                previous = cdf[index];
            }
        }

        internal static void Update(Span<ushort> cdf, int symbol)
        {
            int symbols = cdf.Length - 1;
            int rate = 3 + (cdf[symbols] > 15 ? 1 : 0) + (cdf[symbols] > 31 ? 1 : 0) + Math.Min(BitOperations.Log2((uint)symbols), 2);
            for (int index = 0; index < symbols - 1; index++)
            {
                int probability = cdf[index];
                cdf[index] = checked((ushort)(index < symbol ? probability - (probability >> rate)
                    : probability + ((Top - probability) >> rate)));
            }
            if (cdf[symbols] < 32) { cdf[symbols]++; }
        }
    }
}
