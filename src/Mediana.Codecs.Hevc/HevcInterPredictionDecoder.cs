// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcInterPredictionDecoder
    {
        private readonly HevcSliceHeader slice;
        private readonly HevcCabacReader reader;
        private readonly DecodedPicture picture;
        private readonly CodingBlockMap blocks;
        private readonly HevcMotionField motion;
        private readonly HevcReferenceLists references;
        private readonly HevcMotionCompensator compensator;
        private readonly HevcInterSyntax syntax;
        private readonly HevcMotionPredictor predictor;
        private readonly HevcMergeCandidates merge;

        public HevcInterPredictionDecoder(HevcSliceHeader slice, HevcCabacReader reader, DecodedPicture picture,
            CodingBlockMap blocks, HevcMotionField motion, HevcReferenceLists references, int order, HevcMotionCompensator compensator)
        {
            this.slice = slice; this.reader = reader; this.picture = picture; this.blocks = blocks;
            this.motion = motion; this.references = references; this.compensator = compensator;
            syntax = new(slice, reader);
            HevcMotionNeighbours neighbours = new(slice, motion, blocks.Partitions);
            predictor = new(slice, order, references, neighbours);
            merge = new(slice, references, neighbours, predictor);
        }

        public HevcPartitionMode ReadPartition(int log2Size) => syntax.Partition(log2Size);

        public bool Decode(HevcPredictionBlock block, int depth, bool skipped)
        {
            bool merged = skipped || reader.Read(CabacElement.Merge);
            HevcMotionSample sample;
            if (merged) { sample = merge.Get(block, syntax.MergeIndex()); }
            else
            {
                int direction = syntax.Direction(block, depth), reference0 = -1, reference1 = -1;
                HevcMotionVector motion0 = default, motion1 = default;
                if ((direction & 1) != 0)
                {
                    reference0 = syntax.Reference(references.List0.Count);
                    HevcMotionVector difference = syntax.Difference();
                    motion0 = predictor.Predict(block, 0, reference0, reader.Read(CabacElement.MotionPredictor)).Add(difference.X, difference.Y);
                }
                if ((direction & 2) != 0)
                {
                    reference1 = syntax.Reference(references.List1.Count);
                    HevcMotionVector difference = direction == 3 && slice.MvdList1Zero ? default : syntax.Difference();
                    motion1 = predictor.Predict(block, 1, reference1, reader.Read(CabacElement.MotionPredictor)).Add(difference.X, difference.Y);
                }
                sample = references.Create(reference0, motion0, reference1, motion1);
            }
            HevcPredictionReference? first = null, second = null;
            if (sample.Uses(0)) { first = new(references.List0[sample.Reference0].Picture.Pixels, sample.Vector0, sample.Reference0); }
            if (sample.Uses(1)) { second = new(references.List1[sample.Reference1].Picture.Pixels, sample.Vector1, sample.Reference1); }
            compensator.Predict(picture, block.X, block.Y, block.Width, block.Height, first, second, slice.Weights);
            motion.Set(block.X, block.Y, block.Width, block.Height, sample);
            blocks.MarkPrediction(block.X, block.Y, block.Width, block.Height);
            return merged;
        }

        public void MarkIntra(int x, int y, int size) => motion.Set(x, y, size, size, default);

        public void MarkAvailable(int x, int y, int size)
        {
            picture.Luma.MarkAvailable(x, y, size, size);
            if (picture.Cb is not null && picture.Cr is not null)
            {
                int scaleX = slice.Picture.Sequence.SubWidthC, scaleY = slice.Picture.Sequence.SubHeightC;
                picture.Cb.MarkAvailable(x / scaleX, y / scaleY, size / scaleX, size / scaleY);
                picture.Cr.MarkAvailable(x / scaleX, y / scaleY, size / scaleX, size / scaleY);
            }
        }
    }
}
