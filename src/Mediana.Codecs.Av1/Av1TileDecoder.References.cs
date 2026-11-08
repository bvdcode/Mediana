// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private Av1ReferencePair ReadReference(int row, int column, int width, int height, bool skipMode, Av1ReferencePictures pictures)
        {
            if (skipMode)
            {
                if (!Av1ReferenceOrdering.TryGetSkipPair(header.Inter.References, pictures, header.OrderHint, sequence.Tools.OrderHintBits, out Av1ReferencePair pair))
                {
                    throw new InvalidDataException("The AV1 skip-mode reference pair is unavailable.");
                }
                return pair;
            }
            if (Segment.ForcedReference) { return new(Segment.Reference); }
            if (Segment.Skip || Segment.GlobalMotion) { return new(1); }
            Av1MotionBlock top = row > bounds.RowStart ? motionBlocks[(row - 1) * sequence.MiColumns + column] : default;
            Av1MotionBlock prior = column > bounds.ColumnStart ? motionBlocks[row * sequence.MiColumns + column - 1] : default;
            if (header.Inter.SelectReference && Math.Min(width, height) >= 2 &&
                symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CompModeOffset + CompoundReferenceContext(top, prior, row > bounds.RowStart, column > bounds.ColumnStart) * 3, 3)) != 0)
            {
                int typeContext = CompoundDirectionContext(top, prior, row > bounds.RowStart, column > bounds.ColumnStart);
                if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CompRefTypeOffset + typeContext * 3, 3)) == 0)
                {
                    if (ReferenceDecision(Av1DefaultCdfs.UniCompRefOffset, 3, 0, top, prior, 1, 4, 5, 7) != 0) { return new(5, 7); }
                    if (ReferenceDecision(Av1DefaultCdfs.UniCompRefOffset, 3, 1, top, prior, 2, 2, 3, 4) == 0) { return new(1, 2); }
                    return new(1, ReferenceDecision(Av1DefaultCdfs.UniCompRefOffset, 3, 2, top, prior, 3, 3, 4, 4) == 0 ? 3 : 4);
                }
                int first;
                if (ReferenceDecision(Av1DefaultCdfs.CompRefOffset, 3, 0, top, prior, 1, 2, 3, 4) == 0)
                {
                    first = ReferenceDecision(Av1DefaultCdfs.CompRefOffset, 3, 1, top, prior, 1, 1, 2, 2) == 0 ? 1 : 2;
                }
                else { first = ReferenceDecision(Av1DefaultCdfs.CompRefOffset, 3, 2, top, prior, 3, 3, 4, 4) == 0 ? 3 : 4; }
                if (ReferenceDecision(Av1DefaultCdfs.CompBwdRefOffset, 2, 0, top, prior, 5, 6, 7, 7) != 0) { return new(first, 7); }
                return new(first, ReferenceDecision(Av1DefaultCdfs.CompBwdRefOffset, 2, 1, top, prior, 5, 5, 6, 6) == 0 ? 5 : 6);
            }
            if (ReferenceDecision(Av1DefaultCdfs.SingleRefOffset, 6, 0, top, prior, 1, 4, 5, 7) != 0)
            {
                if (ReferenceDecision(Av1DefaultCdfs.SingleRefOffset, 6, 1, top, prior, 5, 6, 7, 7) != 0) { return new(7); }
                return new(ReferenceDecision(Av1DefaultCdfs.SingleRefOffset, 6, 5, top, prior, 5, 5, 6, 6) != 0 ? 6 : 5);
            }
            if (ReferenceDecision(Av1DefaultCdfs.SingleRefOffset, 6, 2, top, prior, 1, 2, 3, 4) != 0)
            {
                return new(ReferenceDecision(Av1DefaultCdfs.SingleRefOffset, 6, 4, top, prior, 3, 3, 4, 4) != 0 ? 4 : 3);
            }
            return new(ReferenceDecision(Av1DefaultCdfs.SingleRefOffset, 6, 3, top, prior, 1, 1, 2, 2) != 0 ? 2 : 1);
        }

        private int ReferenceDecision(int offset, int decisions, int decision, Av1MotionBlock top, Av1MotionBlock prior,
            int firstMinimum, int firstMaximum, int secondMinimum, int secondMaximum)
        {
            int first = CountReferences(top, firstMinimum, firstMaximum) + CountReferences(prior, firstMinimum, firstMaximum);
            int second = CountReferences(top, secondMinimum, secondMaximum) + CountReferences(prior, secondMinimum, secondMaximum);
            int context = first < second ? 0 : first == second ? 1 : 2;
            return symbols.ReadSymbol(probabilities.Slice(offset + (context * decisions + decision) * 3, 3));
        }

        private static int CountReferences(Av1MotionBlock block, int minimum, int maximum) =>
            (block.Reference >= minimum && block.Reference <= maximum ? 1 : 0) +
            (block.SecondReference >= minimum && block.SecondReference <= maximum ? 1 : 0);

        private static int CompoundReferenceContext(Av1MotionBlock top, Av1MotionBlock prior, bool above, bool left)
        {
            bool topSingle = top.SecondReference == 0;
            bool leftSingle = prior.SecondReference == 0;
            if (above && left)
            {
                if (topSingle && leftSingle) { return (top.Reference >= 5 ? 1 : 0) ^ (prior.Reference >= 5 ? 1 : 0); }
                if (topSingle) { return 2 + (top.Reference >= 5 || top.Reference == 0 ? 1 : 0); }
                if (leftSingle) { return 2 + (prior.Reference >= 5 || prior.Reference == 0 ? 1 : 0); }
                return 4;
            }
            if (above) { return topSingle ? top.Reference >= 5 ? 1 : 0 : 3; }
            if (left) { return leftSingle ? prior.Reference >= 5 ? 1 : 0 : 3; }
            return 1;
        }

        private static int CompoundDirectionContext(Av1MotionBlock top, Av1MotionBlock prior, bool above, bool left)
        {
            bool topCompound = above && top.SecondReference != 0;
            bool leftCompound = left && prior.SecondReference != 0;
            bool topSame = topCompound && SameDirection(top.Reference, top.SecondReference);
            bool leftSame = leftCompound && SameDirection(prior.Reference, prior.SecondReference);
            if (above && top.Reference != 0 && left && prior.Reference != 0)
            {
                bool same = SameDirection(top.Reference, prior.Reference);
                if (!topCompound && !leftCompound) { return 1 + (same ? 2 : 0); }
                if (!topCompound) { return leftSame ? 3 + (same ? 1 : 0) : 1; }
                if (!leftCompound) { return topSame ? 3 + (same ? 1 : 0) : 1; }
                if (!topSame && !leftSame) { return 0; }
                if (!topSame || !leftSame) { return 2; }
                return 3 + ((top.Reference == 5) == (prior.Reference == 5) ? 1 : 0);
            }
            if (above && left)
            {
                if (topCompound) { return 1 + (topSame ? 2 : 0); }
                if (leftCompound) { return 1 + (leftSame ? 2 : 0); }
                return 2;
            }
            if (topCompound) { return topSame ? 4 : 0; }
            if (leftCompound) { return leftSame ? 4 : 0; }
            return 2;
        }

        private static bool SameDirection(int first, int second) => (first >= 5) == (second >= 5);
    }
}
