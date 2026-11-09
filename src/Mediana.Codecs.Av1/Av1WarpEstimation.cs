// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1WarpEstimation
    {
        internal static Av1WarpParameters Estimate(ReadOnlySpan<Av1WarpSample> samples, int row, int column,
            int width, int height, Av1MotionBlock motion)
        {
            int middleY = row * 4 + height * 2 - 1;
            int middleX = column * 4 + width * 2 - 1;
            long a00 = 0, a01 = 0, a11 = 0, bx0 = 0, bx1 = 0, by0 = 0, by1 = 0;
            foreach (Av1WarpSample sample in samples)
            {
                long sy = sample.Row - middleY * 8;
                long sx = sample.Column - middleX * 8;
                long dy = sample.DestinationRow - middleY * 8 - motion.Row;
                long dx = sample.DestinationColumn - middleX * 8 - motion.Column;
                if (Math.Abs(sx - dx) >= 256 || Math.Abs(sy - dy) >= 256) { continue; }
                a00 += Product(sx, sx) + 8;
                a01 += Product(sx, sy) + 4;
                a11 += Product(sy, sy) + 8;
                bx0 += Product(sx, dx) + 8;
                bx1 += Product(sy, dx) + 4;
                by0 += Product(sx, dy) + 4;
                by1 += Product(sy, dy) + 8;
            }
            long determinant = a00 * a11 - a01 * a01;
            if (determinant == 0) { return default; }
            int factor = ResolveDivisor(determinant, out int shift);
            shift -= 16;
            if (shift < 0) { factor <<= -shift; shift = 0; }
            int a = Diagonal(a11 * bx0 - a01 * bx1, factor, shift);
            int b = NonDiagonal(-a01 * bx0 + a00 * bx1, factor, shift);
            int c = NonDiagonal(a11 * by0 - a01 * by1, factor, shift);
            int d = Diagonal(-a01 * by0 + a00 * by1, factor, shift);
            int x = (int)Math.Clamp((long)motion.Column * 8192 - ((long)middleX * (a - 65536) + (long)middleY * b), -8388608, 8388607);
            int y = (int)Math.Clamp((long)motion.Row * 8192 - ((long)middleX * c + (long)middleY * (d - 65536)), -8388608, 8388607);
            return Av1WarpParameters.Create(x, y, a, b, c, d);
        }

        internal static int ResolveDivisor(long value, out int shift)
        {
            ulong magnitude = (ulong)Math.Abs(value);
            int bits = BitOperations.Log2(magnitude);
            ulong remainder = magnitude - (1UL << bits);
            int index = bits > 8 ? (int)((remainder + (1UL << (bits - 9))) >> (bits - 8)) : (int)(remainder << (8 - bits));
            shift = bits + 14;
            int factor = Av1WarpTables.Divisors[index];
            return value < 0 ? -factor : factor;
        }

        internal static long RoundSigned(long value, int bits)
        {
            if (bits == 0) { return value; }
            long rounded = (Math.Abs(value) + (1L << (bits - 1))) >> bits;
            return value < 0 ? -rounded : rounded;
        }

        private static long Product(long a, long b) => ((a * b) >> 2) + a + b;
        private static int NonDiagonal(long value, int factor, int shift) => (int)Math.Clamp(RoundSigned(value * factor, shift), -8191, 8191);
        private static int Diagonal(long value, int factor, int shift) => (int)Math.Clamp(RoundSigned(value * factor, shift), 65536 - 8191, 65536 + 8191);
    }
}
