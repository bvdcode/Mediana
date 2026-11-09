// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1LosslessTransform
    {
        internal static void Inverse(Span<int> coefficients, int bitDepth = 8)
        {
            int sampleMaximum = bitDepth switch
            {
                8 => 255,
                10 => 1023,
                12 => 4095,
                _ => throw new ArgumentOutOfRangeException(nameof(bitDepth))
            };
            int columnMaximum = (1 << (Math.Max(bitDepth + 6, 16) - 1)) - 1;
            for (int row = 0; row < 4; row++)
            {
                Walsh(coefficients.Slice(row * 4, 4), 2);
            }
            Span<int> column = stackalloc int[4];
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    column[y] = Math.Clamp(coefficients[y * 4 + x], -columnMaximum - 1, columnMaximum);
                }
                Walsh(column, 0);
                for (int y = 0; y < 4; y++)
                {
                    if (column[y] < -sampleMaximum - 1 || column[y] > sampleMaximum)
                    {
                        throw new InvalidDataException("The AV1 lossless residual exceeds its signed sample precision.");
                    }
                    coefficients[y * 4 + x] = column[y];
                }
            }
        }

        private static void Walsh(Span<int> values, int shift)
        {
            int a = values[0] >> shift;
            int c = values[1] >> shift;
            int d = values[2] >> shift;
            int b = values[3] >> shift;
            a += c;
            d -= b;
            int e = (a - d) >> 1;
            b = e - b;
            c = e - c;
            a -= b;
            d += c;
            values[0] = a;
            values[1] = b;
            values[2] = c;
            values[3] = d;
        }
    }
}
