// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcMotionPredictor(HevcSliceHeader slice, int order, HevcReferenceLists references, HevcMotionNeighbours neighbours)
    {
        private readonly bool allInPast = references.AllReferencesInPast(order);

        public HevcMotionVector Predict(HevcPredictionBlock block, int list, int index, bool second)
        {
            HevcReferenceEntry target = references.Get(list, index);
            Span<HevcMotionSample> left = stackalloc HevcMotionSample[2];
            left[0] = neighbours.Get(block, block.X - 1, block.Y + block.Height);
            left[1] = neighbours.Get(block, block.X - 1, block.Y + block.Height - 1);
            Span<HevcMotionSample> top = stackalloc HevcMotionSample[3];
            top[0] = neighbours.Get(block, block.X + block.Width, block.Y - 1);
            top[1] = neighbours.Get(block, block.X + block.Width - 1, block.Y - 1);
            top[2] = neighbours.Get(block, block.X - 1, block.Y - 1);
            bool leftAvailable = left[0].IsInter || left[1].IsInter;
            bool foundA = Find(left, target, list, false, out HevcMotionVector a) || Find(left, target, list, true, out a);
            bool foundB = Find(top, target, list, false, out HevcMotionVector b);
            if (!leftAvailable)
            {
                if (foundB) { a = b; foundA = true; }
                foundB = Find(top, target, list, true, out b);
            }
            Span<HevcMotionVector> candidates = stackalloc HevcMotionVector[2];
            int count = 0;
            if (foundA) { candidates[count++] = a; }
            if (foundB && (!foundA || a != b)) { candidates[count++] = b; }
            if (count < 2 && Temporal(block, list, index, out HevcMotionVector temporal)) { candidates[count++] = temporal; }
            while (count < 2) { candidates[count++] = default; }
            return candidates[second ? 1 : 0];
        }

        private bool Find(ReadOnlySpan<HevcMotionSample> candidates, HevcReferenceEntry target, int list, bool scale,
            out HevcMotionVector vector)
        {
            foreach (HevcMotionSample candidate in candidates)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    int direction = pass == 0 ? list : 1 - list;
                    if (!candidate.Uses(direction)) { continue; }
                    (int sourceOrder, int _, HevcMotionVector source, bool longTerm) = candidate.Direction(direction);
                    if (!scale && sourceOrder != target.Picture.Order || scale && longTerm != target.LongTerm) { continue; }
                    vector = source;
                    if (scale && !longTerm && sourceOrder != target.Picture.Order)
                    { vector = source.Scale(Distance(order, target.Picture.Order), Distance(order, sourceOrder)); }
                    return true;
                }
            }
            vector = default; return false;
        }

        public bool Temporal(HevcPredictionBlock block, int list, int index, out HevcMotionVector vector)
        {
            vector = default;
            if (!slice.TemporalMvp) { return false; }
            int collocatedList = slice.CollocatedFromList0 ? 0 : 1;
            HevcReferencePicture collocated = references.Get(collocatedList, slice.CollocatedReference).Picture;
            HevcReferenceEntry target = references.Get(list, index);
            int x = block.X + block.Width, y = block.Y + block.Height;
            if ((block.Y >> slice.Picture.Sequence.Log2Ctb) == (y >> slice.Picture.Sequence.Log2Ctb) &&
                x < collocated.Pixels.Sequence.Width && y < collocated.Pixels.Sequence.Height &&
                Collocated(collocated, target, collocated.Motion.Get(x, y), list, out vector)) { return true; }
            return Collocated(collocated, target, collocated.Motion.Get(block.X + block.Width / 2, block.Y + block.Height / 2), list, out vector);
        }

        private bool Collocated(HevcReferencePicture collocated, HevcReferenceEntry target, HevcMotionSample sample,
            int list, out HevcMotionVector vector)
        {
            vector = default;
            if (!sample.IsInter) { return false; }
            int direction;
            if (!sample.Uses(0)) { direction = 1; }
            else if (!sample.Uses(1)) { direction = 0; }
            else if (allInPast) { direction = list; }
            else { direction = slice.CollocatedFromList0 ? 1 : 0; }
            (int referenceOrder, int _, HevcMotionVector motion, bool longTerm) = sample.Direction(direction);
            if (longTerm != target.LongTerm) { return false; }
            int from = Distance(collocated.Order, referenceOrder), to = Distance(order, target.Picture.Order);
            vector = !longTerm && from != to ? motion.Scale(to, from) : motion;
            return true;
        }

        private static int Distance(int picture, int reference) => (int)Math.Clamp((long)picture - reference, -128, 127);
    }
}
