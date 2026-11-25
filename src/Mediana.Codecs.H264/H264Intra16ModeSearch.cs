// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264Intra16ModeSearch
    {
        public static int Select(H264SourceMacroblock source, H264Picture target, int x, int y, Span<byte> prediction)
        {
            return Select(source, target, x, y, prediction, out _);
        }

        /// <summary>When the minimum is zero, the prediction span contains the selected luma samples.</summary>
        public static int Select(H264SourceMacroblock source, H264Picture target, int x, int y, Span<byte> prediction, out int minimum)
        {
            if (Vector128.IsHardwareAccelerated && target.SampleBytes == 1 && !target.Layout.MacroblockAdaptiveFrameField)
            {
                Span<byte> top = stackalloc byte[16];
                Span<byte> left = stackalloc byte[16];
                if (target.TryReadLuma16Neighbours(x, y, top, left, out byte corner))
                {
                    return SelectInterior(source.Luma, top, left, corner, target.MaximumSample, prediction, out minimum);
                }
            }
            int selected = 2;
            minimum = int.MaxValue;
            for (int mode = 0; mode < 4; mode++)
            {
                bool available = mode switch
                {
                    0 => target.IsInCurrentSlice(x, y - 1),
                    1 => target.IsInCurrentSlice(x - 1, y),
                    2 => true,
                    3 => target.IsInCurrentSlice(x, y - 1) && target.IsInCurrentSlice(x - 1, y) && target.IsInCurrentSlice(x - 1, y - 1),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode))
                };
                if (!available) { continue; }
                target.PredictMacroblock(0, x, y, mode, prediction);
                int difference = H264SampleDifference.Sum16x16(source.Luma, prediction, 16, minimum);
                if (difference == 0) { minimum = 0; return mode; }
                if (difference < minimum) { minimum = difference; selected = mode; }
            }
            return selected;
        }

        private static int SelectInterior(ReadOnlySpan<byte> source, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left,
            byte corner, int maximum, Span<byte> prediction, out int minimum)
        {
            Vector128<byte> vertical = Vector128.Create(top);
            Vector128<byte> horizontal = Vector128.Create(left);
            int sum = Vector128.Sum(Vector128.WidenLower(vertical) + Vector128.WidenUpper(vertical)
                + Vector128.WidenLower(horizontal) + Vector128.WidenUpper(horizontal));
            Vector128<byte> dc = Vector128.Create((byte)((sum + 16) >> 5));
            Vector128<ushort> verticalCost = Vector128<ushort>.Zero;
            Vector128<ushort> horizontalCost = Vector128<ushort>.Zero;
            Vector128<ushort> dcCost = Vector128<ushort>.Zero;
            for (int row = 0; row < 16; row++)
            {
                Vector128<byte> input = Vector128.Create(source[(row * 16)..]);
                verticalCost += H264SampleDifference.Accumulate16(input, vertical);
                horizontalCost += H264SampleDifference.Accumulate16(input, Vector128.Create(left[row]));
                dcCost += H264SampleDifference.Accumulate16(input, dc);
            }

            int selected = 0;
            minimum = H264SampleDifference.Total(verticalCost);
            int horizontalDifference = H264SampleDifference.Total(horizontalCost);
            if (horizontalDifference < minimum)
            {
                selected = 1;
                minimum = horizontalDifference;
            }
            int dcDifference = H264SampleDifference.Total(dcCost);
            if (dcDifference < minimum)
            {
                selected = 2;
                minimum = dcDifference;
            }
            if (minimum == 0)
            {
                source.CopyTo(prediction);
                return selected;
            }

            H264IntraPrediction<byte>.Luma16x16(3, top, left, corner, prediction, maximum);
            int planeDifference = H264SampleDifference.Sum16x16(source, prediction, 16, minimum);
            if (planeDifference < minimum)
            {
                minimum = planeDifference;
                return 3;
            }
            return selected;
        }
    }
}
