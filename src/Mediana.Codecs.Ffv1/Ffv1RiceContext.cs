// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ffv1
{
    internal struct Ffv1RiceContext
    {
        internal int Count;
        internal int Drift;
        internal int Bias;
        internal int Error;

        internal static Ffv1RiceContext Initial => new() { Count = 1, Error = 4 };

        internal int Read(scoped ref Ffv1RiceReader bits)
        {
            int k = 0;
            for (int estimate = Count; estimate < Error; estimate <<= 1)
            {
                k++;
            }
            int residual = bits.ReadSigned(k);
            if (2 * Drift < -Count)
            {
                residual = ~residual;
            }
            int sampleDifference = unchecked((sbyte)(residual + Bias));
            Error += Math.Abs(residual);
            Drift += residual;
            if (Count == 128)
            {
                Count >>= 1;
                Drift >>= 1;
                Error >>= 1;
            }
            Count++;
            if (Drift <= -Count)
            {
                Bias = Math.Max(Bias - 1, -128);
                Drift = Math.Max(Drift + Count, 1 - Count);
            }
            else if (Drift > 0)
            {
                Bias = Math.Min(Bias + 1, 127);
                Drift = Math.Min(Drift - Count, 0);
            }
            return sampleDifference;
        }
    }
}
