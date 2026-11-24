// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264LumaOffsetMotionCheck
    {
        private const int TileCount = 4;
        private const int PatchSize = 4;
        private const int MotionRadius = 2;

        public static bool ImprovesPrediction(VideoFrame input, H264Picture reference, int offset, CancellationToken token)
        {
            int width = input.Format.Width;
            int height = input.Format.Height;
            int columns = Math.Clamp(width / PatchSize, 1, TileCount);
            int rows = Math.Clamp(height / PatchSize, 1, TileCount);
            int patchWidth = Math.Min(PatchSize, width);
            int patchHeight = Math.Min(PatchSize, height);
            int sourceStride = reference.Stride(0);
            ReadOnlySpan<byte> pixels = reference.ReadPlane(0);
            Span<byte> samples = stackalloc byte[PatchSize * PatchSize];
            int unweighted = 0;
            int weighted = 0;
            for (int row = 0; row < rows; row++)
            {
                token.ThrowIfCancellationRequested();
                int y = (2 * row + 1) * (height - patchHeight) / (2 * rows);
                for (int column = 0; column < columns; column++)
                {
                    int x = (2 * column + 1) * (width - patchWidth) / (2 * columns);
                    for (int line = 0; line < patchHeight; line++)
                    {
                        input.GetRow(0, y + line).Slice(x, patchWidth).CopyTo(samples[(line * patchWidth)..]);
                    }
                    (int baseError, int shiftedError) = MinimumErrors(samples, patchWidth, patchHeight, pixels, sourceStride,
                        width, height, x, y, offset);
                    unweighted += baseError;
                    weighted += shiftedError;
                }
            }
            int improvement = unweighted - weighted;
            int count = rows * columns * patchWidth * patchHeight;
            return improvement * 20 > unweighted && improvement * 4 >= count;
        }

        private static (int Unweighted, int Weighted) MinimumErrors(ReadOnlySpan<byte> samples, int patchWidth, int patchHeight,
            ReadOnlySpan<byte> reference, int stride, int width, int height, int originX, int originY, int offset)
        {
            int unweighted = int.MaxValue;
            int weighted = int.MaxValue;
            int firstX = Math.Max(0, originX - MotionRadius);
            int firstY = Math.Max(0, originY - MotionRadius);
            int lastX = Math.Min(width - patchWidth, originX + MotionRadius);
            int lastY = Math.Min(height - patchHeight, originY + MotionRadius);
            for (int y = firstY; y <= lastY; y++)
            {
                for (int x = firstX; x <= lastX; x++)
                {
                    int regular = 0;
                    int corrected = 0;
                    for (int row = 0; row < patchHeight; row++)
                    {
                        ReadOnlySpan<byte> pixels = reference.Slice((y + row) * stride + x, patchWidth);
                        ReadOnlySpan<byte> source = samples.Slice(row * patchWidth, patchWidth);
                        for (int column = 0; column < patchWidth; column++)
                        {
                            int value = pixels[column];
                            regular += Math.Abs(source[column] - value);
                            corrected += Math.Abs(source[column] - Math.Clamp(value + offset, byte.MinValue, byte.MaxValue));
                        }
                    }
                    unweighted = Math.Min(unweighted, regular);
                    weighted = Math.Min(weighted, corrected);
                }
            }
            return (unweighted, weighted);
        }
    }
}
