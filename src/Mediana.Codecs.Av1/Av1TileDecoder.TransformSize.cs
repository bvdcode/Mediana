// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private Av1TransformSize ReadTransformSize(int row, int column, int width, int height, bool allowSelect)
        {
            if (lossless) { return new(4, 4); }
            Av1TransformSize size = new(Math.Min(width * 4, 64), Math.Min(height * 4, 64));
            if (size.Index == 0 || !allowSelect || !header.SelectTransform) { return size; }
            int topWidth = 0;
            int leftHeight = 0;
            if (row > bounds.RowStart)
            {
                Av1BlockContext<T> context = above[column];
                bool inter = !motionBlocks.IsEmpty && motionBlocks[(row - 1) * sequence.MiColumns + column].IsInter;
                topWidth = inter ? 4 << context.WidthLog : context.TransformWidth;
            }
            if (column > bounds.ColumnStart)
            {
                Av1BlockContext<T> context = left[row & (LeftContexts - 1)];
                bool inter = !motionBlocks.IsEmpty && motionBlocks[row * sequence.MiColumns + column - 1].IsInter;
                leftHeight = inter ? 4 << context.HeightLog : context.TransformHeight;
            }
            int selected = (topWidth >= size.Width ? 1 : 0) + (leftHeight >= size.Height ? 1 : 0);
            int offset = size.Maximum switch
            {
                1 => Av1DefaultCdfs.Tx8Offset,
                2 => Av1DefaultCdfs.Tx16Offset,
                3 => Av1DefaultCdfs.Tx32Offset,
                4 => Av1DefaultCdfs.Tx64Offset,
                _ => throw new InvalidDataException("The AV1 transform depth is invalid.")
            };
            int alphabet = size.Maximum == 1 ? 3 : 4;
            int depth = symbols.ReadSymbol(probabilities.Slice(offset + selected * alphabet, alphabet));
            for (int index = 0; index < depth; index++) { size = size.Split; }
            return size;
        }
    }
}
