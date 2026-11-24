// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    internal readonly record struct AreaSampleRange(int First, int Last, long FirstWeight, long LastWeight)
    {
        public long Weight(int sample, long interiorWeight) => sample == First ? FirstWeight : sample == Last ? LastWeight : interiorWeight;

        public static bool FitsGatherWindow(ReadOnlySpan<AreaSampleRange> ranges, int packedWidth, int lanes,
            int sourceWidth, CancellationToken token)
        {
            const int window = 16;
            if (sourceWidth < window || packedWidth == 0)
            {
                return false;
            }
            for (int x = 0; x < packedWidth; x += lanes)
            {
                if ((x & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                int start = Math.Min(ranges[x].First, sourceWidth - window);
                if (ranges[x + lanes - 1].Last - start >= window)
                {
                    return false;
                }
            }
            for (int x = 0; x < packedWidth; x++)
            {
                if ((x & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (ranges[x].Last - ranges[x].First > 2)
                {
                    return false;
                }
            }
            return true;
        }

        public static long CommonUnit(ReadOnlySpan<AreaSampleRange> ranges, long interior, long total, CancellationToken token)
        {
            long unit = Gcd(interior, total);
            for (int i = 0; i < ranges.Length && unit > 1; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                AreaSampleRange range = ranges[i];
                unit = Gcd(unit, range.FirstWeight);
                if (range.Last != range.First)
                {
                    unit = Gcd(unit, range.LastWeight);
                }
            }
            return unit;
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            return a;
        }

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
