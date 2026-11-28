// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264FractionalCross
    {
        private const int Block = 16;

        internal static (int Left, int Right, int Above, int Below) Difference(ReadOnlySpan<byte> source,
            ReadOnlySpan<byte> horizontal, int horizontalStride, ReadOnlySpan<byte> vertical, int verticalStride,
            ReadOnlySpan<byte> center, int centerStride, int cutoff)
        {
            ref byte original = ref MemoryMarshal.GetReference(source);
            ref byte horizontalSamples = ref MemoryMarshal.GetReference(horizontal);
            ref byte verticalSamples = ref MemoryMarshal.GetReference(vertical);
            ref byte centerSamples = ref MemoryMarshal.GetReference(center);
            bool averaged = !center.IsEmpty;
            Vector128<ushort> left = Vector128<ushort>.Zero;
            Vector128<ushort> right = Vector128<ushort>.Zero;
            Vector128<ushort> above = Vector128<ushort>.Zero;
            Vector128<ushort> below = Vector128<ushort>.Zero;
            Vector128<byte> upperRow = Vector128.LoadUnsafe(ref verticalSamples);
            for (int group = 0; group < Block; group += 4)
            {
                for (int row = group; row < group + 4; row++)
                {
                    Vector128<byte> input = Vector128.LoadUnsafe(ref original, (nuint)(row * Block));
                    int horizontalOffset = row * horizontalStride;
                    Vector128<byte> leftRow = Vector128.LoadUnsafe(ref horizontalSamples, (nuint)horizontalOffset);
                    Vector128<byte> rightRow = Vector128.LoadUnsafe(ref horizontalSamples, (nuint)(horizontalOffset + 1));
                    Vector128<byte> lowerRow = Vector128.LoadUnsafe(ref verticalSamples, (nuint)((row + 1) * verticalStride));
                    if (averaged)
                    {
                        Vector128<byte> middle = Vector128.LoadUnsafe(ref centerSamples, (nuint)(row * centerStride));
                        leftRow = H264SampleAverage.Rounded(leftRow, middle);
                        rightRow = H264SampleAverage.Rounded(rightRow, middle);
                        left += H264SampleDifference.Accumulate16(input, leftRow);
                        right += H264SampleDifference.Accumulate16(input, rightRow);
                        above += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(upperRow, middle));
                        below += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(lowerRow, middle));
                    }
                    else
                    {
                        left += H264SampleDifference.Accumulate16(input, leftRow);
                        right += H264SampleDifference.Accumulate16(input, rightRow);
                        above += H264SampleDifference.Accumulate16(input, upperRow);
                        below += H264SampleDifference.Accumulate16(input, lowerRow);
                    }
                    upperRow = lowerRow;
                }

                if (H264SampleDifference.Total(left) > cutoff && H264SampleDifference.Total(right) > cutoff &&
                    H264SampleDifference.Total(above) > cutoff && H264SampleDifference.Total(below) > cutoff)
                {
                    break;
                }
            }
            return (H264SampleDifference.Total(left), H264SampleDifference.Total(right),
                H264SampleDifference.Total(above), H264SampleDifference.Total(below));
        }
    }
}
