// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dv
{
    internal static class DvInverseTransform
    {
        private const int BlockSide = 8;
        private const int RowShift = 11;
        private const int ColumnShift = 20;
        private const int FieldColumnShift = 17;

        internal static void Write(Span<int> coefficients, Span<byte> target, int stride, bool fieldTransform)
        {
            if (coefficients[1..].IndexOfAnyExcept(0) < 0)
            {
                byte dc = Clip((coefficients[0] + 4) >> 3);
                for (int row = 0; row < BlockSide; row++)
                {
                    target.Slice(row * stride, BlockSide).Fill(dc);
                }
                return;
            }
            if (fieldTransform)
            {
                for (int row = 0; row < BlockSide; row += 2)
                {
                    Span<int> even = coefficients.Slice(row * BlockSide, BlockSide);
                    Span<int> odd = coefficients.Slice((row + 1) * BlockSide, BlockSide);
                    for (int x = 0; x < BlockSide; x++)
                    {
                        int a = even[x];
                        int b = odd[x];
                        even[x] = unchecked((short)(a + b));
                        odd[x] = unchecked((short)(a - b));
                    }
                }
            }

            for (int row = 0; row < BlockSide; row++)
            {
                Span<int> values = coefficients.Slice(row * BlockSide, BlockSide);
                if (values[1..].IndexOfAnyExcept(0) < 0)
                {
                    values.Fill(unchecked((short)(values[0] << 3)));
                }
                else
                {
                    TransformEight(values, 1, 1 << (RowShift - 1), RowShift);
                }
            }
            for (int x = 0; x < BlockSide; x++)
            {
                if (fieldTransform)
                {
                    WriteFieldColumn(coefficients[x..], target[x..], 2 * stride);
                    WriteFieldColumn(coefficients[(BlockSide + x)..], target[(stride + x)..], 2 * stride);
                }
                else
                {
                    const int columnRound = 16383 * ((1 << (ColumnShift - 1)) / 16383);
                    TransformEight(coefficients[x..], BlockSide, columnRound, ColumnShift);
                    for (int row = 0; row < BlockSide; row++)
                    {
                        target[row * stride + x] = Clip(coefficients[row * BlockSide + x]);
                    }
                }
            }
        }

        private static void TransformEight(Span<int> values, int step, int rounding, int shift)
        {
            const int c1 = 22725;
            const int c2 = 21407;
            const int c3 = 19266;
            const int c4 = 16383;
            const int c5 = 12873;
            const int c6 = 8867;
            const int c7 = 4520;

            int dc = c4 * values[0] + rounding;
            int even4 = c4 * values[4 * step];
            int even2 = values[2 * step];
            int even6 = values[6 * step];
            int outer = c2 * even2 + c6 * even6;
            int inner = c6 * even2 - c2 * even6;
            int a = values[step], b = values[3 * step], c = values[5 * step], d = values[7 * step];

            WritePair(values, step, 0, dc + even4 + outer, c1 * a + c3 * b + c5 * c + c7 * d, shift);
            WritePair(values, step, 1, dc - even4 + inner, c3 * a - c7 * b - c1 * c - c5 * d, shift);
            WritePair(values, step, 2, dc - even4 - inner, c5 * a - c1 * b + c7 * c + c3 * d, shift);
            WritePair(values, step, 3, dc + even4 - outer, c7 * a - c5 * b + c3 * c - c1 * d, shift);
        }

        private static void WritePair(Span<int> values, int step, int i, int even, int odd, int shift)
        {
            values[i * step] = unchecked((short)((even + odd) >> shift));
            values[(7 - i) * step] = unchecked((short)((even - odd) >> shift));
        }

        private static void WriteFieldColumn(ReadOnlySpan<int> values, Span<byte> target, int stride)
        {
            const int step = 2 * BlockSide;
            const int round = 1 << (FieldColumnShift - 1);
            int evenSum = 2048 * (values[0] + values[2 * step]) + round;
            int evenDifference = 2048 * (values[0] - values[2 * step]) + round;
            int oddSum = 2676 * values[step] + 1108 * values[3 * step];
            int oddDifference = 1108 * values[step] - 2676 * values[3 * step];
            target[0] = Clip((evenSum + oddSum) >> FieldColumnShift);
            target[stride] = Clip((evenDifference + oddDifference) >> FieldColumnShift);
            target[2 * stride] = Clip((evenDifference - oddDifference) >> FieldColumnShift);
            target[3 * stride] = Clip((evenSum - oddSum) >> FieldColumnShift);
        }

        private static byte Clip(int sample) => (byte)Math.Clamp(sample, 0, 255);
    }
}
