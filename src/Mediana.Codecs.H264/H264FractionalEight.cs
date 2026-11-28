// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264FractionalEight
    {
        internal static (int UpperLeft, int Above, int UpperRight, int Left, int Right, int LowerLeft, int Below, int LowerRight)
            Difference(ReadOnlySpan<byte> source, ReadOnlySpan<byte> horizontal, int horizontalStride,
                ReadOnlySpan<byte> vertical, int verticalStride, ReadOnlySpan<byte> center, int centerStride)
        {
            ref byte original = ref MemoryMarshal.GetReference(source);
            ref byte horizontalSamples = ref MemoryMarshal.GetReference(horizontal);
            ref byte verticalSamples = ref MemoryMarshal.GetReference(vertical);
            ref byte centerSamples = ref MemoryMarshal.GetReference(center);
            Vector128<ushort> upperLeft = Vector128<ushort>.Zero;
            Vector128<ushort> above = Vector128<ushort>.Zero;
            Vector128<ushort> upperRight = Vector128<ushort>.Zero;
            Vector128<ushort> left = Vector128<ushort>.Zero;
            Vector128<ushort> right = Vector128<ushort>.Zero;
            Vector128<ushort> lowerLeft = Vector128<ushort>.Zero;
            Vector128<ushort> below = Vector128<ushort>.Zero;
            Vector128<ushort> lowerRight = Vector128<ushort>.Zero;
            Vector128<byte> upperRow = Vector128.LoadUnsafe(ref verticalSamples);
            for (int row = 0; row < 16; row++)
            {
                Vector128<byte> input = Vector128.LoadUnsafe(ref original, (nuint)(row * 16));
                int horizontalOffset = row * horizontalStride;
                Vector128<byte> leftRow = Vector128.LoadUnsafe(ref horizontalSamples, (nuint)horizontalOffset);
                Vector128<byte> rightRow = Vector128.LoadUnsafe(ref horizontalSamples, (nuint)(horizontalOffset + 1));
                Vector128<byte> lowerRow = Vector128.LoadUnsafe(ref verticalSamples, (nuint)((row + 1) * verticalStride));
                Vector128<byte> middle = Vector128.LoadUnsafe(ref centerSamples, (nuint)(row * centerStride));
                upperLeft += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(upperRow, leftRow));
                above += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(upperRow, middle));
                upperRight += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(upperRow, rightRow));
                left += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(leftRow, middle));
                right += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(rightRow, middle));
                lowerLeft += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(lowerRow, leftRow));
                below += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(lowerRow, middle));
                lowerRight += H264SampleDifference.Accumulate16(input, H264SampleAverage.Rounded(lowerRow, rightRow));
                upperRow = lowerRow;
            }
            return (H264SampleDifference.Total(upperLeft), H264SampleDifference.Total(above), H264SampleDifference.Total(upperRight),
                H264SampleDifference.Total(left), H264SampleDifference.Total(right), H264SampleDifference.Total(lowerLeft),
                H264SampleDifference.Total(below), H264SampleDifference.Total(lowerRight));
        }
    }
}
