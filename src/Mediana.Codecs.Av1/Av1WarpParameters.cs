// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1WarpParameters(int X, int Y, int A, int B, int C, int D,
        int Alpha = 0, int Beta = 0, int Gamma = 0, int Delta = 0)
    {
        internal bool IsValid => A != 0;

        internal static Av1WarpParameters Create(int x, int y, int a, int b, int c, int d)
        {
            if (a <= 0) { return default; }
            int factor = Av1WarpEstimation.ResolveDivisor(a, out int shift);
            int alpha = Math.Clamp(a - 65536, short.MinValue, short.MaxValue);
            int beta = Math.Clamp(b, short.MinValue, short.MaxValue);
            int gamma = (int)Math.Clamp(Av1WarpEstimation.RoundSigned(((long)c << 16) * factor, shift), short.MinValue, short.MaxValue);
            int delta = (int)Math.Clamp(d - Av1WarpEstimation.RoundSigned((long)b * c * factor, shift) - 65536, short.MinValue, short.MaxValue);
            alpha = (int)Av1WarpEstimation.RoundSigned(alpha, 6) * 64;
            beta = (int)Av1WarpEstimation.RoundSigned(beta, 6) * 64;
            gamma = (int)Av1WarpEstimation.RoundSigned(gamma, 6) * 64;
            delta = (int)Av1WarpEstimation.RoundSigned(delta, 6) * 64;
            if (4 * Math.Abs(alpha) + 7 * Math.Abs(beta) >= 65536 ||
                4 * Math.Abs(gamma) + 4 * Math.Abs(delta) >= 65536) { return default; }
            return new(x, y, a, b, c, d, alpha, beta, gamma, delta);
        }
    }
}
