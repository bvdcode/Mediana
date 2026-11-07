// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    internal readonly record struct AreaSampleRange(int First, int Last, long FirstWeight, long LastWeight)
    {
        public long Weight(int sample, long interiorWeight) => sample == First ? FirstWeight : sample == Last ? LastWeight : interiorWeight;

        public static void Fill(Span<AreaSampleRange> output, int sourceSize, int phaseQuarters, CancellationToken cancellationToken)
            => Fill(output, sourceSize, sourceSize, 1, phaseQuarters, cancellationToken);

        public static void Fill(Span<AreaSampleRange> output, int sourceSize, int extentNumerator,
            int extentDenominator, int phaseQuarters, CancellationToken cancellationToken)
        {
            long denominator = 4L * output.Length * extentDenominator;
            for (int index = 0; index < output.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Int128 center = (Int128)4 * index * extentNumerator + (Int128)phaseQuarters * (extentNumerator - (long)output.Length * extentDenominator);
                Int128 left = center - 2L * extentNumerator;
                Int128 right = center + 2L * extentNumerator;
                int first = (int)Int128.Clamp(FloorDivide(left + denominator / 2, denominator), 0, sourceSize - 1);
                int last = (int)Int128.Clamp(-FloorDivide(-(right + denominator / 2), denominator) - 1, 0, sourceSize - 1);
                long firstWeight = checked((long)(Int128.Min(right, first == sourceSize - 1 ? right : (Int128)first * denominator + denominator / 2)
                    - Int128.Max(left, first == 0 ? left : (Int128)first * denominator - denominator / 2)));
                long lastWeight = checked((long)(Int128.Min(right, last == sourceSize - 1 ? right : (Int128)last * denominator + denominator / 2)
                    - Int128.Max(left, last == 0 ? left : (Int128)last * denominator - denominator / 2)));
                output[index] = new(first, last, firstWeight, lastWeight);
            }
        }

        private static Int128 FloorDivide(Int128 numerator, long denominator)
        {
            Int128 quotient = numerator / denominator;
            return numerator % denominator < 0 ? quotient - 1 : quotient;
        }
    }
}
