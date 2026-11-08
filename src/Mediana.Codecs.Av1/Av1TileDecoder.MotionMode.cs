// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private int ReadMotionMode(int row, int column, int width, int height, int reference, Av1ReferencePictures pictures)
        {
            int blockIndex = BlockIndex(width, height);
            if (sequence.Tools.InterIntra && blockIndex is >= 3 and <= 9)
            {
                int sizeGroup = BlockSizeGroup(blockIndex);
                if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterIntraOffset + (sizeGroup - 1) * 3, 3)) != 0)
                {
                    throw new NotSupportedException("AV1 inter-intra blending is not implemented.");
                }
            }
            if (!header.Inter.SwitchableMotion || Math.Min(width, height) < 2 || !HasOverlap(row, column, width, height)) { return 0; }
            Av1SequenceHeader source = pictures.Get(header.Inter.References[reference - 1]).Header.Geometry;
            int scaleX = ((source.Width << 14) + sequence.Width / 2) / sequence.Width;
            int scaleY = ((source.Height << 14) + sequence.Height / 2) / sequence.Height;
            bool warped = !header.Inter.IntegerMotion && header.Inter.WarpedMotion && scaleX == 16384 && scaleY == 16384 &&
                HasWarpSamples(row, column, width, height, reference);
            int mode = warped
                ? symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MotionModeOffset + blockIndex * 4, 4))
                : symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.ObmcOffset + blockIndex * 3, 3));
            if (mode == 2) { throw new NotSupportedException($"AV1 warped block reconstruction is not implemented at ({row}, {column})."); }
            return mode;
        }

        private bool HasOverlap(int row, int column, int width, int height)
        {
            if (row > 0)
            {
                for (int x = column; x < Math.Min(sequence.MiColumns, column + width); x += 2)
                {
                    if (motionBlocks[(row - 1) * sequence.MiColumns + (x | 1)].Reference != 0) { return true; }
                }
            }
            if (column > 0)
            {
                for (int y = row; y < Math.Min(sequence.MiRows, row + height); y += 2)
                {
                    if (motionBlocks[(y | 1) * sequence.MiColumns + column - 1].Reference != 0) { return true; }
                }
            }
            return false;
        }

        private bool HasWarpSamples(int row, int column, int width, int height, int reference)
        {
            bool topLeft = true;
            bool topRight = true;
            if (row > 0)
            {
                int sourceWidth = motionBlocks[(row - 1) * sequence.MiColumns + column].Width;
                if (sourceWidth == 0) { throw new InvalidDataException("The AV1 upper motion block is unavailable."); }
                if (width <= sourceWidth)
                {
                    int offset = -(column & (sourceWidth - 1));
                    topLeft = offset == 0;
                    topRight = offset + sourceWidth <= width;
                    if (WarpSample(row - 1, column, reference)) { return true; }
                }
                else
                {
                    for (int index = 0; index < Math.Min(width, sequence.MiColumns - column);)
                    {
                        if (WarpSample(row - 1, column + index, reference)) { return true; }
                        sourceWidth = motionBlocks[(row - 1) * sequence.MiColumns + column + index].Width;
                        if (sourceWidth == 0) { throw new InvalidDataException("The AV1 upper motion block is unavailable."); }
                        index += Math.Min(width, sourceWidth);
                    }
                }
            }
            if (column > 0)
            {
                int sourceHeight = motionBlocks[row * sequence.MiColumns + column - 1].Height;
                if (sourceHeight == 0) { throw new InvalidDataException("The AV1 left motion block is unavailable."); }
                if (height <= sourceHeight)
                {
                    if ((row & (sourceHeight - 1)) != 0) { topLeft = false; }
                    if (WarpSample(row, column - 1, reference)) { return true; }
                }
                else
                {
                    for (int index = 0; index < Math.Min(height, sequence.MiRows - row);)
                    {
                        if (WarpSample(row + index, column - 1, reference)) { return true; }
                        sourceHeight = motionBlocks[(row + index) * sequence.MiColumns + column - 1].Height;
                        if (sourceHeight == 0) { throw new InvalidDataException("The AV1 left motion block is unavailable."); }
                        index += Math.Min(height, sourceHeight);
                    }
                }
            }
            if (topLeft && WarpSample(row - 1, column - 1, reference)) { return true; }
            return topRight && Math.Max(width, height) <= 16 && WarpSample(row - 1, column + width, reference);
        }

        private bool WarpSample(int row, int column, int reference) =>
            row >= 0 && row < sequence.MiRows && column >= 0 && column < sequence.MiColumns &&
            motionBlocks[row * sequence.MiColumns + column].Reference == reference &&
            motionBlocks[row * sequence.MiColumns + column].SecondReference == 0;
    }
}
