// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Applies factored four-point DCT or DST equations with HEVC rounding and clipping.
    /// </summary>
    internal static class IntegerTransform4
    {
        private const int Size = 4;

        public static void Inverse(ReadOnlySpan<int> coefficients, Span<int> residual,
            int bitDepth, bool sine, TransformWorkspace workspace)
        {
            for (int i = 0; i < (Size * Size); i++)
            {
                if ((coefficients[i] < short.MinValue) || (coefficients[i] > short.MaxValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(coefficients));
                }
            }

            Span<int> intermediate = workspace.Intermediate;
            Span<int> sums = stackalloc int[Size];
            for (int x = 0; x < Size; x++)
            {
                Multiply(coefficients[x], coefficients[Size + x], coefficients[(2 * Size) + x],
                    coefficients[(3 * Size) + x], sums, sine);
                for (int y = 0; y < Size; y++)
                {
                    intermediate[(y * Size) + x] = Math.Max(short.MinValue, Math.Min(short.MaxValue, (sums[y] + 64) >> 7));
                }
            }

            int shift = 20 - bitDepth;
            int offset = 1 << (shift - 1);
            for (int y = 0; y < Size; y++)
            {
                ReadOnlySpan<int> input = intermediate.Slice(y * Size, Size);
                Span<int> output = residual.Slice(y * Size, Size);
                Multiply(input[0], input[1], input[2], input[3], output, sine);
                for (int x = 0; x < Size; x++)
                {
                    output[x] = (output[x] + offset) >> shift;
                }
            }
        }

        private static void Multiply(int a, int b, int c, int d, Span<int> output, bool sine)
        {
            if (sine)
            {
                int first = a + c;
                int second = c + d;
                int third = a - d;
                int fourth = 74 * b;
                output[0] = (29 * first) + (55 * second) + fourth;
                output[1] = ((55 * third) - (29 * second)) + fourth;
                output[2] = 74 * ((a - c) + d);
                output[3] = ((55 * first) + (29 * third)) - fourth;
            }
            else
            {
                int evenFirst = 64 * (a + c);
                int evenSecond = 64 * (a - c);
                int oddFirst = (83 * b) + (36 * d);
                int oddSecond = (36 * b) - (83 * d);
                output[0] = evenFirst + oddFirst;
                output[1] = evenSecond + oddSecond;
                output[2] = evenSecond - oddSecond;
                output[3] = evenFirst - oddFirst;
            }
        }
    }
}
