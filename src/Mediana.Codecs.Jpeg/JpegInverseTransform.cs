// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegInverseTransform
    {
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
                        sum += coefficients[index] * (double)quantization[index] * JpegTransform.Basis[x * 8 + u];
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
                        sum += intermediate[v * 8 + x] * JpegTransform.Basis[y * 8 + v];
                    }
                    output[y * stride + x] = Clamp((int)Math.Floor(sum + 128.5));
                }
            }
        }

        private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }
}
