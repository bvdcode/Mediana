// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegTransform
    {
        private static readonly double[] Values = CreateBasis();
        internal static ReadOnlySpan<double> Basis => Values;
        internal static ReadOnlySpan<byte> Zigzag =>
        [
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        ];

        private static double[] CreateBasis()
        {
            double[] values = new double[64];
            for (int x = 0; x < 8; x++)
            {
                for (int frequency = 0; frequency < 8; frequency++)
                {
                    double scale = frequency == 0 ? Math.Sqrt(0.5) : 1;
                    values[x * 8 + frequency] = scale * Math.Cos((2 * x + 1) * frequency * Math.PI / 16) / 2;
                }
            }
            return values;
        }
    }
}
