// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcMergeCandidates(HevcSliceHeader slice, HevcReferenceLists references,
        HevcMotionNeighbours neighbours, HevcMotionPredictor predictor)
    {
        private static ReadOnlySpan<byte> Combined0 => [0, 1, 0, 2, 1, 2, 0, 3, 1, 3, 2, 3];
        private static ReadOnlySpan<byte> Combined1 => [1, 0, 2, 0, 2, 1, 3, 0, 3, 1, 3, 2];

        public HevcMotionSample Get(HevcPredictionBlock block, int index)
        {
            int maximum = slice.MaximumMergeCandidates;
            if ((uint)index >= maximum) { throw new InvalidDataException("Invalid HEVC merge candidate index."); }
            bool restrictBi = block.Width + block.Height == 12;
            if (slice.Picture.Log2ParallelMergeLevel > 2 && block.CodingSize == 8)
            {
                block = block with { X = block.CodingX, Y = block.CodingY, Width = 8, Height = 8, Index = 0, Mode = HevcPartitionMode.Whole };
            }
            Span<HevcMotionSample> candidates = stackalloc HevcMotionSample[5];
            int count = Spatial(block, candidates, maximum);
            if (count < maximum)
            {
                bool first = predictor.Temporal(block, 0, 0, out HevcMotionVector motion0);
                HevcMotionVector motion1 = default;
                bool second = slice.Type == HevcSliceType.B && predictor.Temporal(block, 1, 0, out motion1);
                if (first || second) { candidates[count++] = references.Create(first ? 0 : -1, motion0, second ? 0 : -1, motion1); }
            }
            int original = count;
            if (slice.Type == HevcSliceType.B && original > 1 && original < maximum)
            {
                for (int pair = 0; pair < original * (original - 1) && count < maximum; pair++)
                {
                    HevcMotionSample first = candidates[Combined0[pair]], second = candidates[Combined1[pair]];
                    if (first.Uses(0) && second.Uses(1) &&
                        (first.PictureOrder0 != second.PictureOrder1 || first.Vector0 != second.Vector1))
                    { candidates[count++] = references.Create(first.Reference0, first.Vector0, second.Reference1, second.Vector1); }
                }
            }
            int zero = 0;
            int referenceCount = slice.Type switch
            {
                HevcSliceType.P => references.List0.Count,
                HevcSliceType.B => Math.Min(references.List0.Count, references.List1.Count),
                _ => throw new InvalidDataException("An HEVC intra slice has no merging candidates.")
            };
            while (count < maximum)
            {
                int reference = zero < referenceCount ? zero : 0;
                int reference1 = slice.Type == HevcSliceType.B ? reference : -1;
                candidates[count++] = references.Create(reference, default, reference1, default); zero++;
            }
            HevcMotionSample result = candidates[index];
            if (restrictBi && result.Uses(0) && result.Uses(1))
            { result = references.Create(result.Reference0, result.Vector0, -1, default); }
            return result;
        }

        private int Spatial(HevcPredictionBlock block, Span<HevcMotionSample> output, int maximum)
        {
            HevcMotionSample a1 = default, b1 = default;
            if (block.Index != 1 || block.Mode is not (HevcPartitionMode.Vertical or HevcPartitionMode.NarrowLeft or HevcPartitionMode.NarrowRight))
            { a1 = neighbours.Merge(block, block.X - 1, block.Y + block.Height - 1); }
            if (block.Index != 1 || block.Mode is not (HevcPartitionMode.Horizontal or HevcPartitionMode.NarrowTop or HevcPartitionMode.NarrowBottom))
            { b1 = neighbours.Merge(block, block.X + block.Width - 1, block.Y - 1); }
            int count = 0;
            if (a1.IsInter) { output[count++] = a1; }
            if (count < maximum && b1.IsInter && (!a1.IsInter || !b1.SamePrediction(a1))) { output[count++] = b1; }
            if (count < maximum)
            {
                HevcMotionSample b0 = neighbours.Merge(block, block.X + block.Width, block.Y - 1);
                if (b0.IsInter && (!b1.IsInter || !b0.SamePrediction(b1))) { output[count++] = b0; }
            }
            if (count < maximum)
            {
                HevcMotionSample a0 = neighbours.Merge(block, block.X - 1, block.Y + block.Height);
                if (a0.IsInter && (!a1.IsInter || !a0.SamePrediction(a1))) { output[count++] = a0; }
            }
            if (count < maximum && count < 4)
            {
                HevcMotionSample b2 = neighbours.Merge(block, block.X - 1, block.Y - 1);
                if (b2.IsInter && (!a1.IsInter || !b2.SamePrediction(a1)) && (!b1.IsInter || !b2.SamePrediction(b1)))
                { output[count++] = b2; }
            }
            return count;
        }
    }
}
