// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal class AreaPlaneFilter(long horizontalUnit, long verticalUnit, long horizontalInterior,
        long verticalInterior, ushort denominator)
    {
        // A compact gather occupies less space than its eight scalar ranges. Unpacked tail ranges follow it.
        public static AreaPlaneFilter? Compile(Span<AreaSampleRange> horizontal, ReadOnlySpan<AreaSampleRange> vertical,
            int sourceWidth, int frameWidth, int frameHeight, int extentDenominator, CancellationToken token)
        {
            int packedWidth = horizontal.Length / AreaSampleGather.Lanes * AreaSampleGather.Lanes;
            if (!Ssse3.IsSupported || sourceWidth < 16 || packedWidth == 0)
            {
                return null;
            }
            long horizontalInterior = 4L * horizontal.Length * extentDenominator;
            long verticalInterior = 4L * vertical.Length * extentDenominator;
            long unitX = CommonUnit(horizontal, horizontalInterior, 4L * frameWidth, token);
            long unitY = CommonUnit(vertical, verticalInterior, 4L * frameHeight, token);
            long weightX = 4L * frameWidth / unitX;
            long weightY = 4L * frameHeight / unitY;
            if (weightX > 127 || weightY > 255 / weightX)
            {
                return null;
            }
            for (int x = 0; x < packedWidth; x += AreaSampleGather.Lanes)
            {
                if ((x & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                int start = Math.Min(horizontal[x].First, sourceWidth - 16);
                if (horizontal[x + AreaSampleGather.Lanes - 1].Last - start >= 16)
                {
                    return null;
                }
            }
            for (int i = 0; i < horizontal.Length; i++)
            {
                if ((i & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                AreaSampleRange range = horizontal[i];
                if (range.Last - range.First > 2)
                {
                    return null;
                }
            }
            Span<AreaSampleGather> kernels = MemoryMarshal.Cast<AreaSampleRange, AreaSampleGather>(horizontal[..packedWidth]);
            for (int x = 0; x < packedWidth; x += AreaSampleGather.Lanes)
            {
                if ((x & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                kernels[x / AreaSampleGather.Lanes] = new(horizontal[x..], sourceWidth, unitX, horizontalInterior);
            }
            return new(unitX, unitY, horizontalInterior, verticalInterior, (ushort)(weightX * weightY));
        }

        private static long CommonUnit(ReadOnlySpan<AreaSampleRange> ranges, long interior, long total, CancellationToken token)
        {
            long unit = Gcd(interior, total);
            for (int i = 0; i < ranges.Length; i++)
            {
                if ((i & 1023) == 0) { token.ThrowIfCancellationRequested(); }
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

        public void Render(VideoFrame input, int plane, Span<byte> output, int stride,
            ReadOnlySpan<AreaSampleRange> horizontal, ReadOnlySpan<AreaSampleRange> vertical,
            Span<ushort> scratch, CancellationToken token)
        {
            int width = horizontal.Length;
            int packedWidth = width / AreaSampleGather.Lanes * AreaSampleGather.Lanes;
            ReadOnlySpan<AreaSampleGather> gathers = MemoryMarshal.Cast<AreaSampleRange, AreaSampleGather>(horizontal[..packedWidth]);
            Span<ushort> filtered = scratch[..width];
            Span<ushort> sums = scratch.Slice(width, width);
            int cached = -1;
            for (int y = 0; y < vertical.Length; y++)
            {
                AreaSampleRange range = vertical[y];
                sums.Clear();
                for (int row = range.First; row <= range.Last; row++)
                {
                    token.ThrowIfCancellationRequested();
                    if (cached != row)
                    {
                        ReadOnlySpan<byte> samples = input.GetRow(plane, row);
                        for (int x = 0; x < packedWidth; x += AreaSampleGather.Lanes)
                        {
                            if ((x & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                            gathers[x / AreaSampleGather.Lanes].Filter(samples).StoreUnsafe(ref MemoryMarshal.GetReference(filtered), (nuint)x);
                        }
                        for (int x = packedWidth; x < width; x++)
                        {
                            AreaSampleRange columns = horizontal[x];
                            long sum = 0;
                            for (int column = columns.First; column <= columns.Last; column++)
                            {
                                sum += columns.Weight(column, horizontalInterior) * samples[column];
                            }
                            filtered[x] = (ushort)(sum / horizontalUnit);
                        }
                        cached = row;
                    }
                    ushort weight = (ushort)(range.Weight(row, verticalInterior) / verticalUnit);
                    int columnIndex = 0;
                    for (; columnIndex < packedWidth; columnIndex += AreaSampleGather.Lanes)
                    {
                        Vector128<ushort> values = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(filtered), (nuint)columnIndex);
                        Vector128<ushort> prior = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(sums), (nuint)columnIndex);
                        (prior + values * weight).StoreUnsafe(ref MemoryMarshal.GetReference(sums), (nuint)columnIndex);
                    }
                    for (; columnIndex < width; columnIndex++)
                    {
                        sums[columnIndex] += (ushort)(filtered[columnIndex] * weight);
                    }
                }
                WriteRow(sums, output.Slice(y * stride, width), packedWidth);
            }
        }

        private void WriteRow(ReadOnlySpan<ushort> sums, Span<byte> output, int packedWidth)
        {
            Vector128<ushort> half = Vector128.Create((ushort)(denominator / 2));
            Vector128<ushort> divisor = Vector128.Create(denominator);
            Vector128<ushort> reciprocal = Vector128.Create(denominator == 1 ? (ushort)0 : (ushort)(65536 / denominator));
            int x = 0;
            for (; x < packedWidth; x += AreaSampleGather.Lanes)
            {
                Vector128<ushort> sum = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(sums), (nuint)x) + half;
                Vector128<ushort> value = sum;
                if (denominator != 1)
                {
                    // The low reciprocal can underestimate a 16-bit quotient by at most one.
                    value = Sse2.MultiplyHigh(sum, reciprocal);
                    value -= Vector128.GreaterThanOrEqual(sum - value * divisor, divisor);
                }
                Vector128.Narrow(value, Vector128<ushort>.Zero).GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)x);
            }
            for (; x < sums.Length; x++)
            {
                output[x] = (byte)((sums[x] + denominator / 2) / denominator);
            }
        }
    }
}
