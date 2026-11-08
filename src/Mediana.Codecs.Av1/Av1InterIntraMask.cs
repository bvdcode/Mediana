// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1InterIntraMask
    {
        internal static int Weight(int mode, int width, int height, int row, int column)
        {
            int scale = 128 / Math.Max(width, height);
            return mode switch
            {
                0 => 32,
                1 => Weights[row * scale],
                2 => Weights[column * scale],
                3 => Weights[Math.Min(row, column) * scale],
                _ => throw new InvalidDataException("The AV1 inter-intra mask mode is invalid.")
            };
        }

        private static ReadOnlySpan<byte> Weights =>
        [
            60, 58, 56, 54, 52, 50, 48, 47, 45, 44, 42, 41, 39, 38, 37, 35,
            34, 33, 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 22, 21, 20,
            19, 19, 18, 18, 17, 16, 16, 15, 15, 14, 14, 13, 13, 12, 12, 12,
            11, 11, 10, 10, 10, 9, 9, 9, 8, 8, 8, 8, 7, 7, 7, 7,
            6, 6, 6, 6, 6, 5, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4,
            4, 4, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
        ];
    }
}
