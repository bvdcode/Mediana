// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private Av1TransformSize ReadInterTransformSizes(int row, int column, int width, int height, bool skip)
        {
            if (header.SelectTransform && (width != 1 || height != 1) && !skip && !lossless)
            {
                Av1TransformSize maximum = new(Math.Min(width * 4, 64), Math.Min(height * 4, 64));
                for (int y = row; y < row + height; y += maximum.Height4)
                {
                    for (int x = column; x < column + width; x += maximum.Width4)
                    {
                        ReadVariableTransformSize(y, x, row, column, width, height, maximum, 0);
                    }
                }
                return maximum;
            }
            Av1TransformSize size = ReadTransformSize(row, column, width, height, !skip);
            StoreTransformSize(row, column, width, height, size);
            return size;
        }

        private void ReadVariableTransformSize(int row, int column, int ownerRow, int ownerColumn,
            int width, int height, Av1TransformSize size, int depth)
        {
            if (row >= sequence.MiRows || column >= sequence.MiColumns) { return; }
            bool split = false;
            if (size.Index != 0 && depth < 2)
            {
                int topWidth;
                if (row == ownerRow && row == bounds.RowStart) { topWidth = 64; }
                else
                {
                    Av1MotionBlock top = motionBlocks[(row - 1) * sequence.MiColumns + column];
                    topWidth = row == ownerRow && top.Reference != 0 && above[column].Skip != 0
                        ? top.Width * 4 : Av1TransformSize.FromIndex(top.TransformSize).Width;
                }
                int leftHeight;
                if (column == ownerColumn && column == bounds.ColumnStart) { leftHeight = 64; }
                else
                {
                    Av1MotionBlock prior = motionBlocks[row * sequence.MiColumns + column - 1];
                    leftHeight = column == ownerColumn && prior.Reference != 0 && left[row & (LeftContexts - 1)].Skip != 0
                        ? prior.Height * 4 : Av1TransformSize.FromIndex(prior.TransformSize).Height;
                }
                int maximum = System.Numerics.BitOperations.Log2((uint)Math.Min(64, Math.Max(width, height) * 4)) - 2;
                int context = (size.Maximum != maximum ? 3 : 0) + (4 - maximum) * 6 +
                    (topWidth < size.Width ? 1 : 0) + (leftHeight < size.Height ? 1 : 0);
                split = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.TxfmSplitOffset + context * 3, 3)) != 0;
            }
            if (!split)
            {
                StoreTransformSize(row, column, size.Width4, size.Height4, size);
                return;
            }
            Av1TransformSize child = size.Split;
            for (int y = 0; y < size.Height4; y += child.Height4)
            {
                for (int x = 0; x < size.Width4; x += child.Width4)
                {
                    ReadVariableTransformSize(row + y, column + x, ownerRow, ownerColumn, width, height, child, depth + 1);
                }
            }
        }

        private void StoreTransformSize(int row, int column, int width, int height, Av1TransformSize size)
        {
            for (int y = row; y < row + height && y < sequence.MiRows; y++)
            {
                for (int x = column; x < column + width && x < sequence.MiColumns; x++)
                {
                    motionBlocks[y * sequence.MiColumns + x].TransformSize = (byte)size.Index;
                }
            }
        }
    }
}
