// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal class AreaInt32PlaneFilter(int horizontalInterior, int verticalInterior, int denominator) : IAreaPlaneFilter
    {
        public static AreaInt32PlaneFilter? Compile(Span<AreaSampleRange> horizontal, Span<AreaSampleRange> vertical,
            int sourceWidth, int frameWidth, int frameHeight, int extentDenominator, CancellationToken token)
        {
            const int lanes = AreaInt32Gather.Lanes;
            int packedWidth = horizontal.Length / lanes * lanes;
            if (!Sse41.IsSupported || !Ssse3.IsSupported ||
                !AreaSampleRange.FitsGatherWindow(horizontal, packedWidth, lanes, sourceWidth, token))
            {
                return null;
            }
            long interiorX = 4L * horizontal.Length * extentDenominator;
            long interiorY = 4L * vertical.Length * extentDenominator;
            long unitX = AreaSampleRange.CommonUnit(horizontal, interiorX, 4L * frameWidth, token);
            long unitY = AreaSampleRange.CommonUnit(vertical, interiorY, 4L * frameHeight, token);
            long weightX = 4L * frameWidth / unitX;
            long weightY = 4L * frameHeight / unitY;
            // The rounded sum is below 256 times the product of the two axis weights.
            if (weightX > short.MaxValue || weightY > int.MaxValue / 256 / weightX)
            {
                return null;
            }
            Normalize(horizontal, unitX, token);
            Normalize(vertical, unitY, token);
            Span<AreaInt32Gather> kernels = MemoryMarshal.Cast<AreaSampleRange, AreaInt32Gather>(horizontal[..packedWidth]);
            for (int x = 0; x < packedWidth; x += lanes)
            {
                if ((x & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                kernels[x / lanes] = new(horizontal[x..], sourceWidth);
            }
            return new((int)(interiorX / unitX), (int)(interiorY / unitY), (int)(weightX * weightY));
        }

        private static void Normalize(Span<AreaSampleRange> ranges, long unit, CancellationToken token)
        {
            for (int i = 0; i < ranges.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                ref AreaSampleRange range = ref ranges[i];
                range = range with { FirstWeight = range.FirstWeight / unit, LastWeight = range.LastWeight / unit };
            }
        }

        public void Render(VideoFrame input, int plane, Span<byte> output, int stride, ReadOnlySpan<AreaSampleRange> horizontal,
            ReadOnlySpan<AreaSampleRange> vertical, Span<byte> workspace, CancellationToken token)
        {
            const int lanes = AreaInt32Gather.Lanes;
            int width = horizontal.Length;
            Span<int> scratch = MemoryMarshal.Cast<byte, int>(workspace);
            Span<int> filtered = scratch[..width];
            Span<int> sums = scratch.Slice(width, width);
            int cached = -1;
            int packedWidth = width / lanes * lanes;
            ReadOnlySpan<AreaInt32Gather> kernels = MemoryMarshal.Cast<AreaSampleRange, AreaInt32Gather>(horizontal[..packedWidth]);
            Vector128<uint> divisor = Vector128.Create((uint)denominator);
            Vector128<uint> rounding = Vector128.Create((uint)(denominator / 2));
            Vector128<uint> reciprocal = Vector128.Create(denominator == 1 ? 0 : (uint)((1UL << 32) / (uint)denominator));
            for (int y = 0; y < vertical.Length; y++)
            {
                AreaSampleRange range = vertical[y];
                sums.Clear();
                for (int row = range.First; row <= range.Last; row++)
                {
                    token.ThrowIfCancellationRequested();
                    if (cached != row)
                    {
                        FilterRow(input.GetRow(plane, row), horizontal, kernels, filtered, packedWidth, token);
                        cached = row;
                    }
                    int weight = (int)range.Weight(row, verticalInterior);
                    int column = 0;
                    for (; column < packedWidth; column += lanes)
                    {
                        Vector128<int> value = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(filtered), (nuint)column);
                        Vector128<int> prior = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(sums), (nuint)column);
                        (prior + value * weight).StoreUnsafe(ref MemoryMarshal.GetReference(sums), (nuint)column);
                    }
                    for (; column < width; column++)
                    {
                        sums[column] += weight * filtered[column];
                    }
                }
                Span<byte> line = output.Slice(y * stride, width);
                int index = 0;
                for (; index <= width - 8; index += 8)
                {
                    Vector128<uint> low = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(sums), (nuint)index).AsUInt32() + rounding;
                    Vector128<uint> high = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(sums), (nuint)(index + 4)).AsUInt32() + rounding;
                    if (denominator != 1)
                    {
                        low = Divide(low, reciprocal, divisor);
                        high = Divide(high, reciprocal, divisor);
                    }
                    Vector128<ushort> value = Vector128.Narrow(low, high);
                    Vector128.Narrow(value, Vector128<ushort>.Zero).GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(line), (nuint)index);
                }
                for (; index < width; index++)
                {
                    line[index] = (byte)((sums[index] + denominator / 2) / denominator);
                }
            }
        }

        private void FilterRow(ReadOnlySpan<byte> input, ReadOnlySpan<AreaSampleRange> horizontal,
            ReadOnlySpan<AreaInt32Gather> kernels, Span<int> filtered, int packedWidth, CancellationToken token)
        {
            const int lanes = AreaInt32Gather.Lanes;
            int x = 0;
            for (; x < packedWidth; x += lanes)
            {
                if ((x & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                kernels[x / lanes].Filter(input, horizontalInterior).StoreUnsafe(ref MemoryMarshal.GetReference(filtered), (nuint)x);
            }
            for (; x < horizontal.Length; x++)
            {
                AreaSampleRange columns = horizontal[x];
                int sum = 0;
                for (int column = columns.First; column <= columns.Last; column++)
                {
                    if ((column & 1023) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                    }
                    sum += input[column] * (int)columns.Weight(column, horizontalInterior);
                }
                filtered[x] = sum;
            }
        }

        private static Vector128<uint> Divide(Vector128<uint> value, Vector128<uint> reciprocal, Vector128<uint> divisor)
        {
            // The truncated reciprocal can underestimate the quotient by at most one.
            Vector128<ulong> even = Sse2.ShiftRightLogical(Sse2.Multiply(value, reciprocal), 32);
            Vector128<uint> odd = Sse2.ShiftRightLogical(value.AsUInt64(), 32).AsUInt32();
            Vector128<ulong> high = Sse2.Multiply(odd, reciprocal) & Vector128.Create(0xffffffff00000000UL);
            Vector128<uint> quotient = (even | high).AsUInt32();
            return quotient - Vector128.GreaterThanOrEqual(value - quotient * divisor, divisor);
        }
    }
}
