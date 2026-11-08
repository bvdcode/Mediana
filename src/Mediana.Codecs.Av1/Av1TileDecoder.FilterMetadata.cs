// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void ReadCdef(int row, int column, int width, int height, bool skip)
        {
            if (skip || cdefIndices.IsEmpty) { return; }
            int stride = (sequence.MiColumns + 15) >> 4;
            int index = (row >> 4) * stride + (column >> 4);
            if (cdefIndices[index] >= 0) { return; }
            sbyte value = (sbyte)symbols.ReadLiteral(header.Cdef.IndexBits);
            int endRow = Math.Min(((row & ~15) + height + 15) >> 4, (sequence.MiRows + 15) >> 4);
            int endColumn = Math.Min(((column & ~15) + width + 15) >> 4, stride);
            for (int y = row >> 4; y < endRow; y++)
            {
                cdefIndices.Slice(y * stride + (column >> 4), endColumn - (column >> 4)).Fill(value);
            }
        }

        private void StoreFilterBlock(int row, int column, int width, int height, int reference, int mode, bool skip)
        {
            if (filterBlocks.IsEmpty) { return; }
            Av1FilterBlock block = new() { BlockSize = (byte)BlockIndex(width, height), Deltas = BlockFilterDeltas() };
            block.SetPrediction(reference, mode >= 13 && mode is not (15 or 23), skip, segmentId);
            for (int y = row; y < row + height && y < sequence.MiRows; y++)
            {
                filterBlocks.Slice(y * sequence.MiColumns + column, Math.Min(width, sequence.MiColumns - column)).Fill(block);
            }
        }

        private void StoreFilterTransform(int plane, int x4, int y4, Av1TransformSize size)
        {
            if (filterBlocks.IsEmpty) { return; }
            int sub = sequence.PlaneShift(plane);
            int startX = x4 << sub;
            int startY = y4 << sub;
            int endX = Math.Min((x4 + size.Width4) << sub, sequence.MiColumns);
            int endY = Math.Min((y4 + size.Height4) << sub, sequence.MiRows);
            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    ref Av1FilterBlock block = ref filterBlocks[y * sequence.MiColumns + x];
                    if (plane == 0) { block.LumaTransform = (byte)size.Index; }
                    else { block.ChromaTransform = (byte)size.Index; }
                }
            }
        }
    }
}
