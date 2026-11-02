// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264ForwardQuantizationTestInput
    {
        private static ReadOnlySpan<int> Scan => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        private static ReadOnlySpan<int> Factors => [13107, 8066, 5243, 11916, 7490, 4660, 10082, 6554, 4194,
            9362, 5825, 3647, 8192, 5243, 3355, 7282, 4559, 2893];

        public static void Verify()
        {
            int[] coefficients = new int[16];
            for (int qp = 0; qp <= 51; qp++)
            {
                foreach (int value in new[] { -int.MaxValue, -16384, -255, -1, 0, 1, 255, 16384, int.MaxValue })
                {
                    Array.Fill(coefficients, value);
                    VerifyBlock(coefficients, qp);
                }
                for (int level = 0; level < 64; level++)
                {
                    foreach (int offset in new[] { -1, 0, 1 })
                    {
                        for (int position = 0; position < 16; position++)
                        {
                            long divisor = 1L << (15 + qp / 6);
                            long boundary = (level * divisor - divisor / 3) / Scale(position, qp);
                            coefficients[position] = checked((int)(boundary + offset)) * (position % 2 == 0 ? 1 : -1);
                        }
                        VerifyBlock(coefficients, qp);
                    }
                }
                VerifyChroma(qp);
            }
        }

        private static void VerifyBlock(int[] coefficients, int qp)
        {
            int[] unchanged = (int[])coefficients.Clone();
            foreach (int count in new[] { 15, 16 })
            {
                int[] guarded = new int[count + 2];
                Array.Fill(guarded, 197);
                H264ForwardTransform.Quantize4x4(coefficients, qp, guarded.AsSpan(1, count));
                for (int index = 0; index < count; index++)
                {
                    int position = Scan[index + 16 - count];
                    int expected = Reference(coefficients[position], Scale(position, qp), 15 + qp / 6);
                    if (guarded[index + 1] != expected)
                    {
                        throw new InvalidDataException("Forward quantization differs from the scalar equation at a coefficient or rounding boundary.");
                    }
                }
                if (guarded[0] != 197 || guarded[^1] != 197 || !coefficients.AsSpan().SequenceEqual(unchanged))
                {
                    throw new InvalidDataException("Forward quantization changed source or surrounding output storage.");
                }
            }
        }

        private static void VerifyChroma(int qp)
        {
            ReadOnlySpan<int> matrix = [1, 1, 1, 1, 1, -1, 1, -1, 1, 1, -1, -1, 1, -1, -1, 1];
            int[] dc = new int[4];
            int[] levels = new int[4];
            for (int sample = 0; sample < 64; sample++)
            {
                for (int index = 0; index < dc.Length; index++)
                {
                    dc[index] = (sample * 977 + index * 53) % 8161 - 4080;
                }
                H264ForwardTransform.ChromaDc(dc, qp, levels);
                for (int row = 0; row < 4; row++)
                {
                    int sum = 0;
                    for (int column = 0; column < 4; column++)
                    {
                        sum += matrix[row * 4 + column] * dc[column];
                    }
                    if (levels[row] != Reference(sum, Factors[qp % 6 * 3], 16 + qp / 6))
                    {
                        throw new InvalidDataException("Chroma DC quantization differs from the matrix and scalar equation.");
                    }
                }
            }
        }

        private static int Scale(int position, int qp)
        {
            int row = position / 4;
            int column = position % 4;
            int category = row % 2 + column % 2;
            return Factors[qp % 6 * 3 + category];
        }

        private static int Reference(int value, int scale, int shift)
        {
            long divisor = 1L << shift;
            long magnitude = (Math.Abs((long)value) * scale + divisor / 3) / divisor;
            return checked((int)(Math.Sign(value) * magnitude));
        }
    }
}
