// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcInterSyntax(HevcSliceHeader slice, HevcCabacReader reader)
    {
        public HevcPartitionMode Partition(int log2Size)
        {
            if (reader.Read(CabacElement.PartMode, 0)) { return HevcPartitionMode.Whole; }
            bool horizontal = reader.Read(CabacElement.PartMode, 1);
            SequenceParameters sequence = slice.Picture.Sequence;
            if (log2Size == sequence.Log2MinCodingBlock)
            {
                if (!horizontal && log2Size > 3 && !reader.Read(CabacElement.PartMode, 2)) { return HevcPartitionMode.Quarters; }
            }
            else if (sequence.AsymmetricMotionPartitions && !reader.Read(CabacElement.PartMode, 3))
            {
                bool lastNarrow = reader.Bypass();
                if (horizontal) { return lastNarrow ? HevcPartitionMode.NarrowBottom : HevcPartitionMode.NarrowTop; }
                return lastNarrow ? HevcPartitionMode.NarrowRight : HevcPartitionMode.NarrowLeft;
            }
            return horizontal ? HevcPartitionMode.Horizontal : HevcPartitionMode.Vertical;
        }

        public int Direction(HevcPredictionBlock block, int depth)
        {
            switch (slice.Type)
            {
                case HevcSliceType.P: return 1;
                case HevcSliceType.B:
                    if (block.Width + block.Height != 12 && reader.Read(CabacElement.InterDirection, depth)) { return 3; }
                    return reader.Read(CabacElement.InterDirection, 4) ? 2 : 1;
                default: throw new InvalidDataException("An HEVC intra slice cannot signal inter prediction.");
            }
        }

        public int Reference(int count)
        {
            if (count is < 1 or > 15) { throw new InvalidDataException("Invalid HEVC active reference count."); }
            if (count == 1 || !reader.Read(CabacElement.ReferenceIndex, 0)) { return 0; }
            if (count == 2 || !reader.Read(CabacElement.ReferenceIndex, 1)) { return 1; }
            return 2 + reader.Unary(count - 3);
        }

        public int MergeIndex()
        {
            int count = slice.MaximumMergeCandidates;
            if (count is < 1 or > 5) { throw new InvalidDataException("Invalid HEVC merge candidate count."); }
            if (count == 1 || !reader.Read(CabacElement.MergeIndex)) { return 0; }
            return 1 + reader.Unary(count - 2);
        }

        public HevcMotionVector Difference()
        {
            bool hasX = reader.Read(CabacElement.MotionDifference, 0), hasY = reader.Read(CabacElement.MotionDifference, 0);
            int x = 0, y = 0;
            if (hasX) { x = reader.Read(CabacElement.MotionDifference, 1) ? 2 : 1; }
            if (hasY) { y = reader.Read(CabacElement.MotionDifference, 1) ? 2 : 1; }
            return new(Component(x), Component(y));
        }

        private short Component(int magnitude)
        {
            if (magnitude == 0) { return 0; }
            if (magnitude == 2) { magnitude += reader.ExpGolomb(1); }
            if (reader.Bypass()) { magnitude = -magnitude; }
            if (magnitude is < short.MinValue or > short.MaxValue)
            { throw new InvalidDataException("HEVC motion difference exceeds signed sixteen-bit precision."); }
            return (short)magnitude;
        }
    }
}
