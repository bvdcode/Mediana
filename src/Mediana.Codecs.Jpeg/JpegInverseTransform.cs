// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegInverseTransform
    {
        private static readonly double[] Basis = CreateBasis();
        internal static ReadOnlySpan<byte> Zigzag =>
        [
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        ];

        internal static void Write(ReadOnlySpan<int> coefficients, ReadOnlySpan<int> quantization,
            Span<byte> output, int stride)
        {
            bool dcOnly = true;
            for (int i = 1; i < 64; i++)
            {
                dcOnly &= coefficients[i] == 0;
            }
            if (dcOnly)
            {
                byte sample = Clamp((int)Math.Floor(coefficients[0] * (double)quantization[0] / 8 + 128.5));
                for (int y = 0; y < 8; y++)
                {
                    output.Slice(y * stride, 8).Fill(sample);
                }
                return;
            }
            Span<double> intermediate = stackalloc double[64];
            for (int v = 0; v < 8; v++)
            {
                for (int x = 0; x < 8; x++)
                {
                    double sum = 0;
                    for (int u = 0; u < 8; u++)
                    {
                        int index = v * 8 + u;
                        sum += coefficients[index] * (double)quantization[index] * Basis[x * 8 + u];
                    }
                    intermediate[v * 8 + x] = sum;
                }
            }
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    double sum = 0;
                    for (int v = 0; v < 8; v++)
                    {
                        sum += intermediate[v * 8 + x] * Basis[y * 8 + v];
                    }
                    output[y * stride + x] = Clamp((int)Math.Floor(sum + 128.5));
                }
            }
        }

        private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

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
