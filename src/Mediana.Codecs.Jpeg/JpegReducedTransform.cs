// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegReducedTransform
    {
        private static readonly double[] Half = Basis(2);
        private static readonly double[] Quarter = Basis(4);

        internal static void Write(ReadOnlySpan<int> coefficients, ReadOnlySpan<int> quantization,
            Span<byte> output, int stride, int reduction)
        {
            if (reduction == 1)
            {
                JpegInverseTransform.Write(coefficients, quantization, output, stride);
                return;
            }
            int size = 8 / reduction;
            bool dcOnly = true;
            for (int i = 1; i < 64; i++) { dcOnly &= coefficients[i] == 0; }
            if (reduction == 8 || dcOnly)
            {
                byte sample = Sample(coefficients[0] * (double)quantization[0] / 8);
                for (int y = 0; y < size; y++) { output.Slice(y * stride, size).Fill(sample); }
                return;
            }
            double[] basis = reduction switch
            {
                2 => Half,
                4 => Quarter,
                _ => throw new ArgumentOutOfRangeException(nameof(reduction))
            };
            Span<double> intermediate = stackalloc double[32];
            for (int v = 0; v < 8; v++)
            {
                for (int x = 0; x < size; x++)
                {
                    double sum = 0;
                    for (int u = 0; u < 8; u++)
                    {
                        int index = v * 8 + u;
                        sum += coefficients[index] * (double)quantization[index] * basis[x * 8 + u];
                    }
                    intermediate[v * size + x] = sum;
                }
            }
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double sum = 0;
                    for (int v = 0; v < 8; v++) { sum += intermediate[v * size + x] * basis[y * 8 + v]; }
                    output[y * stride + x] = Sample(sum);
                }
            }
        }

        private static byte Sample(double value) => (byte)Math.Clamp((int)Math.Floor(value + 128.5), 0, 255);

        private static double[] Basis(int reduction)
        {
            double[] values = new double[8 * (8 / reduction)];
            for (int x = 0; x < 8 / reduction; x++)
            {
                for (int frequency = 0; frequency < 8; frequency++)
                {
                    double sum = 0;
                    for (int sample = 0; sample < reduction; sample++)
                    {
                        sum += Math.Cos((2 * (x * reduction + sample) + 1) * frequency * Math.PI / 16);
                    }
                    values[x * 8 + frequency] = sum * (frequency == 0 ? Math.Sqrt(0.5) : 1) / (2 * reduction);
                }
            }
            return values;
        }
    }
}
