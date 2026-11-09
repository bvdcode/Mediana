// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1RestorationWiener
    {
        internal static void Filter<T>(Av1RestorationSource<T> source, Span<T> output, int stride, int x, int y, int width, int height, Av1RestorationUnit unit, int bitDepth = 8) where T : unmanaged, IBinaryInteger<T>
        {
            int firstRound = bitDepth switch
            {
                8 or 10 => 3,
                12 => 5,
                _ => throw new ArgumentOutOfRangeException(nameof(bitDepth))
            };
            int secondRound = 14 - firstRound;
            int firstBias = 1 << (firstRound - 1);
            int secondBias = 1 << (secondRound - 1);
            int offset = 1 << (bitDepth + 6 - firstRound);
            int limit = (1 << (bitDepth + 8 - firstRound)) - 1 - offset;
            int maximum = (1 << bitDepth) - 1;
            Span<int> vertical = stackalloc int[7];
            Span<int> horizontal = stackalloc int[7];
            Coefficients(vertical, unit.V0, unit.V1, unit.V2);
            Coefficients(horizontal, unit.H0, unit.H1, unit.H2);
            Span<int> intermediate = stackalloc int[(height + 6) * width];
            for (int row = 0; row < height + 6; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int sum = 0;
                    for (int tap = 0; tap < 7; tap++) { sum += horizontal[tap] * source.Sample(x + column + tap - 3, y + row - 3); }
                    intermediate[row * width + column] = Math.Clamp((sum + firstBias) >> firstRound, -offset, limit);
                }
            }
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int sum = 0;
                    for (int tap = 0; tap < 7; tap++) { sum += vertical[tap] * intermediate[(row + tap) * width + column]; }
                    output[(y + row) * stride + x + column] = T.CreateTruncating(Math.Clamp((sum + secondBias) >> secondRound, 0, maximum));
                }
            }
        }

        private static void Coefficients(Span<int> coefficients, int first, int second, int third)
        {
            coefficients[0] = coefficients[6] = first;
            coefficients[1] = coefficients[5] = second;
            coefficients[2] = coefficients[4] = third;
            coefficients[3] = 128 - 2 * (first + second + third);
        }
    }
}
