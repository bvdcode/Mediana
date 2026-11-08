// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref struct Av1WarpSamples(Span<Av1WarpSample> storage, ReadOnlySpan<Av1MotionBlock> blocks,
        Av1SequenceHeader sequence, int row, int column, int width, int height, Av1MotionBlock motion)
    {
        internal const int Capacity = 8;
        private readonly Span<Av1WarpSample> candidates = storage;
        private readonly ReadOnlySpan<Av1MotionBlock> motionBlocks = blocks;
        private int count;
        private int scanned;
        internal readonly ReadOnlySpan<Av1WarpSample> Samples => candidates[..count];

        internal void Find()
        {
            bool topLeft = true;
            bool topRight = true;
            if (row > 0)
            {
                int sourceWidth = motionBlocks[(row - 1) * sequence.MiColumns + column].Width;
                if (sourceWidth == 0) { throw new InvalidDataException("The AV1 upper warp block is unavailable."); }
                if (width <= sourceWidth)
                {
                    int offset = -(column & (sourceWidth - 1));
                    topLeft = offset == 0;
                    topRight = offset + sourceWidth <= width;
                    Add(row - 1, column);
                }
                else
                {
                    for (int index = 0; index < Math.Min(width, sequence.MiColumns - column);)
                    {
                        sourceWidth = motionBlocks[(row - 1) * sequence.MiColumns + column + index].Width;
                        if (sourceWidth == 0) { throw new InvalidDataException("The AV1 upper warp block is unavailable."); }
                        Add(row - 1, column + index);
                        index += Math.Min(width, sourceWidth);
                    }
                }
            }
            if (column > 0)
            {
                int sourceHeight = motionBlocks[row * sequence.MiColumns + column - 1].Height;
                if (sourceHeight == 0) { throw new InvalidDataException("The AV1 left warp block is unavailable."); }
                if (height <= sourceHeight)
                {
                    if ((row & (sourceHeight - 1)) != 0) { topLeft = false; }
                    Add(row, column - 1);
                }
                else
                {
                    for (int index = 0; index < Math.Min(height, sequence.MiRows - row);)
                    {
                        sourceHeight = motionBlocks[(row + index) * sequence.MiColumns + column - 1].Height;
                        if (sourceHeight == 0) { throw new InvalidDataException("The AV1 left warp block is unavailable."); }
                        Add(row + index, column - 1);
                        index += Math.Min(height, sourceHeight);
                    }
                }
            }
            if (topLeft) { Add(row - 1, column - 1); }
            if (topRight && Math.Max(width, height) <= 16) { Add(row - 1, column + width); }
            if (count == 0 && scanned > 0) { count = 1; }
        }

        private void Add(int candidateRow, int candidateColumn)
        {
            if (scanned == Capacity || candidateRow < 0 || candidateColumn < 0 ||
                candidateRow >= sequence.MiRows || candidateColumn >= sequence.MiColumns) { return; }
            Av1MotionBlock candidate = motionBlocks[candidateRow * sequence.MiColumns + candidateColumn];
            if (candidate.Reference != motion.Reference || candidate.SecondReference != 0 || candidate.InterIntra) { return; }
            int threshold = Math.Clamp(Math.Max(width, height) * 4, 16, 112);
            bool valid = Math.Abs(candidate.Row - motion.Row) + Math.Abs(candidate.Column - motion.Column) <= threshold;
            scanned++;
            if (!valid && scanned > 1) { return; }
            int middleY = (candidateRow & ~(candidate.Height - 1)) * 4 + candidate.Height * 2 - 1;
            int middleX = (candidateColumn & ~(candidate.Width - 1)) * 4 + candidate.Width * 2 - 1;
            candidates[count] = new(middleY * 8, middleX * 8, middleY * 8 + candidate.Row, middleX * 8 + candidate.Column);
            if (valid) { count++; }
        }
    }
}
