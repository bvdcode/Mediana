// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void InterTransformTree(int row, int column, int blockWidth, int blockHeight, int x, int y,
            int width, int height, bool skip, scoped Span<int> residual)
        {
            if (row + y >= sequence.MiRows || column + x >= sequence.MiColumns) { return; }
            cancellationToken.ThrowIfCancellationRequested();
            Av1MotionBlock motion = motionBlocks[(row + y) * sequence.MiColumns + column + x];
            Av1TransformSize selected = Av1TransformSize.FromIndex(motion.TransformSize);
            if (width <= selected.Width && height <= selected.Height)
            {
                Av1TransformSize size = new(width, height);
                TransformBlock(0, row, column, blockWidth, blockHeight, x, y, blockWidth, blockHeight,
                    skip, default, default, default, size, residual[..(width * height)], true);
            }
            else if (width > height)
            {
                InterTransformTree(row, column, blockWidth, blockHeight, x, y, width / 2, height, skip, residual);
                InterTransformTree(row, column, blockWidth, blockHeight, x + width / 8, y, width / 2, height, skip, residual);
            }
            else if (width < height)
            {
                InterTransformTree(row, column, blockWidth, blockHeight, x, y, width, height / 2, skip, residual);
                InterTransformTree(row, column, blockWidth, blockHeight, x, y + height / 8, width, height / 2, skip, residual);
            }
            else
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        InterTransformTree(row, column, blockWidth, blockHeight, x + dx * width / 8, y + dy * height / 8,
                            width / 2, height / 2, skip, residual);
                    }
                }
            }
        }
    }
}
