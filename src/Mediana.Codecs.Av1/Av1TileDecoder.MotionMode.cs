// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private int ReadMotionMode(int row, int column, int width, int height, Av1MotionBlock block, Av1ReferencePictures pictures, out Av1WarpParameters warp)
        {
            warp = default;
            int reference = block.Reference;
            int blockIndex = BlockIndex(width, height);
            if (!header.Inter.SwitchableMotion || Math.Min(width, height) < 2 || !HasOverlap(row, column, width, height)) { return 0; }
            if (!header.Inter.IntegerMotion && block.Mode is 15 or 23 && header.GlobalMotion[reference].Affine) { return 0; }
            Av1SequenceHeader source = pictures.Get(header.Inter.References[reference - 1]).PixelGeometry;
            int scaleX = ((source.Width << 14) + sequence.Width / 2) / sequence.Width;
            int scaleY = ((source.Height << 14) + sequence.Height / 2) / sequence.Height;
            Span<Av1WarpSample> storage = stackalloc Av1WarpSample[Av1WarpSamples.Capacity];
            Av1WarpSamples samples = new(storage, motionBlocks, sequence, bounds, row, column, width, height, block);
            if (!header.Inter.IntegerMotion && header.Inter.WarpedMotion && scaleX == 16384 && scaleY == 16384) { samples.Find(); }
            int mode = !samples.Samples.IsEmpty
                ? symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MotionModeOffset + blockIndex * 4, 4))
                : symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.ObmcOffset + blockIndex * 3, 3));
            if (mode == 2) { warp = Av1WarpEstimation.Estimate(samples.Samples, row, column, width, height, block); }
            return mode;
        }

        private bool HasOverlap(int row, int column, int width, int height)
        {
            if (row > bounds.RowStart)
            {
                for (int x = column; x < Math.Min(sequence.MiColumns, column + width); x += 2)
                {
                    if (motionBlocks[(row - 1) * sequence.MiColumns + (x | 1)].Reference != 0) { return true; }
                }
            }
            if (column > bounds.ColumnStart)
            {
                for (int y = row; y < Math.Min(sequence.MiRows, row + height); y += 2)
                {
                    if (motionBlocks[(y | 1) * sequence.MiColumns + column - 1].Reference != 0) { return true; }
                }
            }
            return false;
        }
    }
}
