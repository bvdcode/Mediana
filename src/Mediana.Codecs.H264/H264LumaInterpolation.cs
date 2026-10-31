// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264LumaInterpolation
    {
        private const int BlockSize = 16;
        private const int Padding = 5;
        private const int FirstTap = 2;

        public static void Apply(ReadOnlySpan<byte> samples, int stride, int rows, long x, long y,
            int dx, int dy, int width, int height, Span<byte> output)
        {
            Span<byte> patch = stackalloc byte[(BlockSize + Padding) * (BlockSize + Padding)];
            Span<int> horizontal = stackalloc int[(BlockSize + Padding) * BlockSize];
            for (int top = 0; top < height;)
            {
                int tileHeight = Math.Min(BlockSize, height - top);
                for (int left = 0; left < width;)
                {
                    int tileWidth = Math.Min(BlockSize, width - left);
                    int patchStride = tileWidth + Padding;
                    H264BorderExtension.Copy(samples, stride, rows, x + left - FirstTap, y + top - FirstTap,
                        patchStride, tileHeight + Padding, patch);
                    PredictTile(patch, patchStride, horizontal, dx, dy, tileWidth, tileHeight, width,
                        output[(top * width + left)..]);
                    left += tileWidth;
                }
                top += tileHeight;
            }
        }

        private static void PredictTile(ReadOnlySpan<byte> patch, int stride, Span<int> horizontal,
            int dx, int dy, int width, int height, int outputStride, Span<byte> output)
        {
            if (dx != 0 && dy != 0 && (dx == 2 || dy == 2))
            {
                for (int row = 0; row < height + Padding; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        horizontal[row * width + column] = HorizontalSum(patch, row * stride + column);
                    }
                }
            }
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int position = (row + FirstTap) * stride + column + FirstTap;
                    int halfPosition = (row + FirstTap) * width + column;
                    int diagonalPosition = row * width + column;
                    // Rec. ITU-T H.264, Table 8-12: clip half samples before quarter-sample averaging.
                    int value = (dx, dy) switch
                    {
                        (0, 0) => patch[position],
                        (1, 0) => Average(patch[position], Horizontal(patch, position)),
                        (2, 0) => Horizontal(patch, position),
                        (3, 0) => Average(Horizontal(patch, position), patch[position + 1]),
                        (0, 1) => Average(patch[position], Vertical(patch, stride, position)),
                        (1, 1) => Average(Horizontal(patch, position), Vertical(patch, stride, position)),
                        (2, 1) => Average(Half(horizontal[halfPosition]), Diagonal(horizontal, width, diagonalPosition)),
                        (3, 1) => Average(Horizontal(patch, position), Vertical(patch, stride, position + 1)),
                        (0, 2) => Vertical(patch, stride, position),
                        (1, 2) => Average(Vertical(patch, stride, position), Diagonal(horizontal, width, diagonalPosition)),
                        (2, 2) => Diagonal(horizontal, width, diagonalPosition),
                        (3, 2) => Average(Diagonal(horizontal, width, diagonalPosition), Vertical(patch, stride, position + 1)),
                        (0, 3) => Average(Vertical(patch, stride, position), patch[position + stride]),
                        (1, 3) => Average(Vertical(patch, stride, position), Horizontal(patch, position + stride)),
                        (2, 3) => Average(Diagonal(horizontal, width, diagonalPosition), Half(horizontal[halfPosition + width])),
                        (3, 3) => Average(Vertical(patch, stride, position + 1), Horizontal(patch, position + stride)),
                        _ => throw new InvalidOperationException("An H.264 luma fraction is outside its two-bit range.")
                    };
                    output[row * outputStride + column] = (byte)value;
                }
            }
        }

        private static int HorizontalSum(ReadOnlySpan<byte> samples, int start) =>
            samples[start] + samples[start + 5] - 5 * (samples[start + 1] + samples[start + 4]) +
            20 * (samples[start + 2] + samples[start + 3]);

        private static int Horizontal(ReadOnlySpan<byte> samples, int position) =>
            Half(HorizontalSum(samples, position - FirstTap));

        private static int Vertical(ReadOnlySpan<byte> samples, int stride, int position)
        {
            int start = position - FirstTap * stride;
            int sum = samples[start] + samples[start + 5 * stride] -
                5 * (samples[start + stride] + samples[start + 4 * stride]) +
                20 * (samples[start + 2 * stride] + samples[start + 3 * stride]);
            return Half(sum);
        }

        private static int Diagonal(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            int sum = horizontal[start] + horizontal[start + 5 * stride] -
                5 * (horizontal[start + stride] + horizontal[start + 4 * stride]) +
                20 * (horizontal[start + 2 * stride] + horizontal[start + 3 * stride]);
            return Math.Clamp((sum + 512) >> 10, 0, 255);
        }

        private static int Half(int value) => Math.Clamp((value + 16) >> 5, 0, 255);
        private static int Average(int a, int b) => (a + b + 1) >> 1;
    }
}
