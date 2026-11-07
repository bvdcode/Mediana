// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static partial class H264LumaInterpolation
    {
        private const int BlockSize = 16;
        private const int Padding = 5;
        private const int FirstTap = 2;

        public static void Apply(ReadOnlySpan<byte> samples, int stride, int rows, long x, long y,
            int dx, int dy, int width, int height, Span<byte> output)
        {
            for (int top = 0; top < height;)
            {
                int tileHeight = Math.Min(BlockSize, height - top);
                for (int left = 0; left < width;)
                {
                    int tileWidth = Math.Min(BlockSize, width - left);
                    long patchX = x + left - FirstTap;
                    long patchY = y + top - FirstTap;
                    Span<byte> destination = output[(top * width + left)..];
                    if (patchX >= 0 && patchX <= stride - tileWidth - Padding &&
                        patchY >= 0 && patchY <= rows - tileHeight - Padding)
                    {
                        PredictTile(samples[((int)patchY * stride + (int)patchX)..], stride, dx, dy,
                            tileWidth, tileHeight, width, destination);
                    }
                    else
                    {
                        PredictBorder(samples, stride, rows, patchX, patchY, dx, dy,
                            tileWidth, tileHeight, width, destination);
                    }
                    left += tileWidth;
                }
                top += tileHeight;
            }
        }

        private static void PredictBorder(ReadOnlySpan<byte> samples, int stride, int rows, long x, long y,
            int dx, int dy, int width, int height, int outputStride, Span<byte> output)
        {
            Span<byte> patch = stackalloc byte[(BlockSize + Padding) * (BlockSize + Padding)];
            int patchStride = width + Padding;
            H264BorderExtension.Copy(samples, stride, rows, x, y, patchStride, height + Padding, patch);
            PredictTile(patch, patchStride, dx, dy, width, height, outputStride, output);
        }

        private static void PredictTile(ReadOnlySpan<byte> patch, int stride, int dx, int dy,
            int width, int height, int outputStride, Span<byte> output)
        {
            int origin = FirstTap * stride + FirstTap;
            switch (dx, dy)
            {
                case (0, 0):
                    for (int row = 0; row < height; row++)
                    {
                        patch.Slice(origin + row * stride, width).CopyTo(output.Slice(row * outputStride, width));
                    }
                    break;
                case (1 or 2 or 3, 0):
                    FilterHalfBlock(patch, stride, origin, 1, width, height, outputStride, output, false);
                    if (dx != 2)
                    {
                        AverageIntegerBlock(patch, stride, origin + (dx >> 1), width, height, outputStride, output);
                    }
                    break;
                case (0, 1 or 2 or 3):
                    FilterHalfBlock(patch, stride, origin, stride, width, height, outputStride, output, false);
                    if (dy != 2)
                    {
                        AverageIntegerBlock(patch, stride, origin + (dy >> 1) * stride, width, height, outputStride, output);
                    }
                    break;
                case (1 or 3, 1 or 3):
                    FilterHalfBlock(patch, stride, origin + (dy >> 1) * stride, 1, width, height, outputStride, output, false);
                    FilterHalfBlock(patch, stride, origin + (dx >> 1), stride, width, height, outputStride, output, true);
                    break;
                case (2, 1 or 2 or 3):
                case (1 or 3, 2):
                    PredictDiagonal(patch, stride, origin, dx, dy, width, height, outputStride, output);
                    break;
                default:
                    throw new InvalidOperationException("An H.264 luma fraction is outside its two-bit range.");
            }
        }

        private static void PredictDiagonal(ReadOnlySpan<byte> patch, int stride, int origin, int dx, int dy,
            int width, int height, int outputStride, Span<byte> output)
        {
            Span<int> horizontal = stackalloc int[(BlockSize + Padding) * BlockSize];
            PrepareHorizontal(patch, stride, width, height, horizontal);
            FilterDiagonalBlock(horizontal, width, height, outputStride, output);
            if (dy != 2)
            {
                AverageHorizontalBlock(horizontal, width, FirstTap + (dy >> 1), height, outputStride, output);
            }
            else if (dx != 2)
            {
                FilterHalfBlock(patch, stride, origin + (dx >> 1), stride, width, height, outputStride, output, true);
            }
        }

        internal static int HorizontalSum(ReadOnlySpan<byte> samples, int start) =>
            H264LumaFilter.Sum(samples, start, 1);

        internal static int Vertical(ReadOnlySpan<byte> samples, int stride, int position) =>
            Half(H264LumaFilter.Sum(samples, position - FirstTap * stride, stride));

        internal static int Diagonal(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            int sum = horizontal[start] + horizontal[start + 5 * stride] -
                5 * (horizontal[start + stride] + horizontal[start + 4 * stride]) +
                20 * (horizontal[start + 2 * stride] + horizontal[start + 3 * stride]);
            return Math.Clamp((sum + 512) >> 10, 0, 255);
        }

        internal static int Half(int value) => Math.Clamp((value + 16) >> 5, 0, 255);
        private static int Average(int a, int b) => (a + b + 1) >> 1;
    }
}
