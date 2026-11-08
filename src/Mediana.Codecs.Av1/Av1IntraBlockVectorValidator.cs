// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1IntraBlockVectorValidator
    {
        private const int MaximumComponent = 1 << 14;
        private const int PipelineDelaySuperblocks = 4;

        internal static bool IsValid(Av1SequenceHeader sequence, Av1TileBounds bounds, int row, int column,
            int width, int height, int motionY, int motionX, bool hasChroma)
        {
            if (motionY <= -MaximumComponent || motionY >= MaximumComponent ||
                motionX <= -MaximumComponent || motionX >= MaximumComponent || ((motionY | motionX) & 7) != 0)
            { return false; }
            int sourceTop = row * 4 + (motionY >> 3);
            int sourceLeft = column * 4 + (motionX >> 3);
            int sourceBottom = sourceTop + height * 4;
            int sourceRight = sourceLeft + width * 4;
            if (hasChroma)
            {
                int subsampling = sequence.PlaneShift(1);
                if (width < 2 && subsampling != 0) { sourceLeft -= 4; }
                if (height < 2 && subsampling != 0) { sourceTop -= 4; }
            }
            if (sourceTop < bounds.RowStart * 4 || sourceLeft < bounds.ColumnStart * 4 ||
                sourceBottom > bounds.RowEnd * 4 || sourceRight > bounds.ColumnEnd * 4)
            { return false; }
            int side = sequence.Superblock128 ? 128 : 64;
            int activeRow = row * 4 / side;
            int activeColumn = column >> 4;
            int sourceRow = (sourceBottom - 1) / side;
            int sourceColumn = (sourceRight - 1) >> 6;
            int columns = ((bounds.ColumnEnd - bounds.ColumnStart - 1) >> 4) + 1;
            int active = activeRow * columns + activeColumn;
            int source = sourceRow * columns + sourceColumn;
            if (source >= active - PipelineDelaySuperblocks) { return false; }
            int gradient = 1 + PipelineDelaySuperblocks + (sequence.Superblock128 ? 1 : 0);
            int offset = gradient * (activeRow - sourceRow);
            return sourceRow <= activeRow && sourceColumn < activeColumn - PipelineDelaySuperblocks + offset;
        }
    }
}
