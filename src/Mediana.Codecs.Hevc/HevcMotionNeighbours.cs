// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcMotionNeighbours(HevcSliceHeader slice, HevcMotionField motion, PicturePartitions? partitions)
    {
        private readonly int log2Ctb = slice.Picture.Sequence.Log2Ctb;
        private readonly int columns = (slice.Picture.Sequence.Width - 1) / (1 << slice.Picture.Sequence.Log2Ctb) + 1;

        public HevcMotionSample Get(HevcPredictionBlock block, int x, int y)
        {
            HevcMotionSample candidate = motion.Get(x, y);
            if (!candidate.IsInter || partitions is not null && !partitions.SamePredictionRegion(x, y, block.X, block.Y)) { return default; }
            bool sameCodingBlock = x >= block.CodingX && x < block.CodingX + block.CodingSize &&
                y >= block.CodingY && y < block.CodingY + block.CodingSize;
            if (sameCodingBlock)
            {
                if (block.Mode == HevcPartitionMode.Quarters && block.Index == 1 &&
                    y >= block.CodingY + block.Height && x < block.CodingX + block.Width) { return default; }
            }
            else
            {
                int address = (y >> log2Ctb) * columns + (x >> log2Ctb);
                int current = (block.Y >> log2Ctb) * columns + (block.X >> log2Ctb);
                if (address > current || address == current && ZOrder(x, y) > ZOrder(block.X, block.Y)) { return default; }
            }
            return candidate;
        }

        public HevcMotionSample Merge(HevcPredictionBlock block, int x, int y)
        {
            int level = slice.Picture.Log2ParallelMergeLevel;
            if ((x >> level) == (block.X >> level) && (y >> level) == (block.Y >> level)) { return default; }
            return Get(block, x, y);
        }

        private int ZOrder(int x, int y)
        {
            int address = 0;
            for (int bit = 2; bit < log2Ctb; bit++)
            {
                address |= ((x >> bit) & 1) << ((bit - 2) * 2);
                address |= ((y >> bit) & 1) << ((bit - 2) * 2 + 1);
            }
            return address;
        }
    }
}
