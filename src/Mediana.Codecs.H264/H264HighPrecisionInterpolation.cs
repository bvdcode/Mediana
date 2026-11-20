// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264HighPrecisionInterpolation
    {
        private const int LumaBlockSize = 16;
        private const int ChromaBlockSize = 8;
        private const int FilterPadding = 5;

        public static void Luma(ReadOnlySpan<ushort> samples, int stride, int columns, int rows, long x, long y,
            int dx, int dy, int width, int height, Span<ushort> output, int maximum)
        {
            for (int top = 0; top < height; top += LumaBlockSize)
            {
                for (int left = 0; left < width; left += LumaBlockSize)
                {
                    LumaTile(samples, stride, columns, rows, x + left, y + top, dx, dy,
                        Math.Min(LumaBlockSize, width - left), Math.Min(LumaBlockSize, height - top),
                        width, output[(top * width + left)..], maximum);
                }
            }
        }

        private static void LumaTile(ReadOnlySpan<ushort> samples, int stride, int columns, int rows, long x, long y,
            int dx, int dy, int width, int height, int outputStride, Span<ushort> output, int maximum)
        {
            int patchStride = width + FilterPadding;
            Span<ushort> patch = stackalloc ushort[(LumaBlockSize + FilterPadding) * (LumaBlockSize + FilterPadding)];
            Copy(samples, stride, columns, rows, x - 2, y - 2, patchStride, height + FilterPadding, patch);
            Span<int> horizontal = stackalloc int[LumaBlockSize * (LumaBlockSize + FilterPadding)];
            bool diagonal = dx != 0 && dy != 0 && (dx == 2 || dy == 2);
            if (diagonal)
            {
                for (int row = 0; row < height + 5; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        horizontal[row * width + column] = Filter(patch, row * patchStride + column + 2, 1);
                    }
                }
            }
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int origin = (row + 2) * patchStride + column + 2;
                    int value;
                    switch (dx, dy)
                    {
                        case (0, 0):
                            value = patch[origin];
                            break;
                        case (1 or 2 or 3, 0):
                            value = Half(patch, origin, 1, maximum);
                            if (dx != 2)
                            {
                                value = (value + patch[origin + (dx >> 1)] + 1) >> 1;
                            }
                            break;
                        case (0, 1 or 2 or 3):
                            value = Half(patch, origin, patchStride, maximum);
                            if (dy != 2)
                            {
                                value = (value + patch[origin + (dy >> 1) * patchStride] + 1) >> 1;
                            }
                            break;
                        case (1 or 3, 1 or 3):
                            value = (Half(patch, origin + (dy >> 1) * patchStride, 1, maximum)
                                + Half(patch, origin + (dx >> 1), patchStride, maximum) + 1) >> 1;
                            break;
                        case (2, 1 or 2 or 3):
                        case (1 or 3, 2):
                            int index = row * width + column;
                            int filtered = horizontal[index] - 5 * horizontal[index + width] + 20 * horizontal[index + 2 * width]
                                + 20 * horizontal[index + 3 * width] - 5 * horizontal[index + 4 * width] + horizontal[index + 5 * width];
                            value = Math.Clamp((filtered + 512) >> 10, 0, maximum);
                            if (dx != 2)
                            {
                                value = (value + Half(patch, origin + (dx >> 1), patchStride, maximum) + 1) >> 1;
                            }
                            else if (dy != 2)
                            {
                                value = (value + Half(patch, origin + (dy >> 1) * patchStride, 1, maximum) + 1) >> 1;
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(dx));
                    }
                    output[row * outputStride + column] = (ushort)value;
                }
            }
        }

        public static void Chroma(ReadOnlySpan<ushort> samples, int stride, int columns, int rows, long x, long y,
            int dx, int dy, int width, int height, Span<ushort> output)
        {
            for (int top = 0; top < height; top += ChromaBlockSize)
            {
                for (int left = 0; left < width; left += ChromaBlockSize)
                {
                    ChromaTile(samples, stride, columns, rows, x + left, y + top, dx, dy,
                        Math.Min(ChromaBlockSize, width - left), Math.Min(ChromaBlockSize, height - top),
                        width, output[(top * width + left)..]);
                }
            }
        }

        private static void ChromaTile(ReadOnlySpan<ushort> samples, int stride, int columns, int rows, long x, long y,
            int dx, int dy, int width, int height, int outputStride, Span<ushort> output)
        {
            int patchStride = width + 1;
            Span<ushort> patch = stackalloc ushort[(ChromaBlockSize + 1) * (ChromaBlockSize + 1)];
            Copy(samples, stride, columns, rows, x, y, patchStride, height + 1, patch);
            int a = (8 - dx) * (8 - dy);
            int b = dx * (8 - dy);
            int c = (8 - dx) * dy;
            int d = dx * dy;
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int index = row * patchStride + column;
                    output[row * outputStride + column] = (ushort)((a * patch[index] + b * patch[index + 1]
                        + c * patch[index + patchStride] + d * patch[index + patchStride + 1] + 32) >> 6);
                }
            }
        }

        public static void Copy(ReadOnlySpan<ushort> samples, int stride, int columns, int rows, long x, long y,
            int width, int height, Span<ushort> output)
        {
            for (int row = 0; row < height; row++)
            {
                int sourceRow = (int)Math.Clamp(y + row, 0, rows - 1) * stride;
                for (int column = 0; column < width; column++)
                {
                    int sourceColumn = (int)Math.Clamp(x + column, 0, columns - 1);
                    output[row * width + column] = samples[sourceRow + sourceColumn];
                }
            }
        }

        private static int Half(ReadOnlySpan<ushort> samples, int position, int step, int maximum) =>
            Math.Clamp((Filter(samples, position, step) + 16) >> 5, 0, maximum);

        private static int Filter(ReadOnlySpan<ushort> samples, int position, int step) =>
            samples[position - 2 * step] - 5 * samples[position - step] + 20 * samples[position]
                + 20 * samples[position + step] - 5 * samples[position + 2 * step] + samples[position + 3 * step];
    }
}
