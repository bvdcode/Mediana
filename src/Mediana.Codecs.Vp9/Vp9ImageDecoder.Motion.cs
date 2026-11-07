// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9
{
    internal ref partial struct Vp9ImageDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private void ReadMotion(ref Vp9BooleanReader bits, int row, int column, ref Vp9BlockInfo block,
            Vp9BlockInfo above, Vp9BlockInfo left, bool hasAbove, bool hasLeft)
        {
            ReadReferences(ref bits, ref block, above, left, hasAbove, hasLeft);
            Vp9MotionPrediction prediction = new(blocks, previous, header, miStride, row, column, block.Size, tile.ColumnStart, tile.ColumnEnd, usePreviousMotion);
            (Vp9MotionVector nearest, Vp9MotionVector near, int context) = prediction.Find(block.ReferenceZero);
            Span<Vp9MotionVector> best = stackalloc Vp9MotionVector[2]; best[0] = nearest;
            if (block.ReferenceOne != 0) { best[1] = prediction.Find(block.ReferenceOne).Nearest; }
            bool forcedZero = header.Segmentation && state.SegmentEnabled[block.Segment * 4 + 3];
            int mode = block.Size >= 3 && !forcedZero ? ReadInterMode(ref bits, context) : 12;
            block.Interpolation = (byte)header.InterpolationFilter;
            if (header.InterpolationFilter == 4)
            {
                int a = hasAbove && above.Inter ? above.Interpolation : 3, l = hasLeft && left.Inter ? left.Interpolation : 3;
                int filterContext = a == l ? a : l == 3 ? a : a == 3 ? l : 3;
                block.Interpolation = (byte)symbols.Tree(ref bits, FilterTree, probabilities, Vp9ProbabilityOffset.Interpolation + filterContext * 2);
            }
            int width = block.Size >= 3 ? 2 : Widths[block.Size] / 4, height = block.Size >= 3 ? 2 : Heights[block.Size] / 4;
            for (int y = 0; y < 2; y += height)
            {
                for (int x = 0; x < 2; x += width)
                {
                    if (block.Size < 3) { mode = ReadInterMode(ref bits, context); }
                    for (int list = 0; list < (block.ReferenceOne == 0 ? 1 : 2); list++)
                    {
                        if (mode is 10 or 11)
                        {
                            if (block.Size < 3) { (nearest, near) = SubMotion(prediction, block, list, y * 2 + x); }
                            else { (nearest, near, _) = prediction.Find(block.Reference(list)); }
                        }
                        Vp9MotionVector value = mode switch
                        {
                            10 => nearest, 11 => near, 12 => default,
                            13 => Vp9MotionReader.Read(ref bits, probabilities, symbols, best[list], header.HighPrecisionMotion),
                            _ => throw new InvalidDataException("Invalid VP9 motion mode.")
                        };
                        for (int dy = 0; dy < height; dy++)
                        { for (int dx = 0; dx < width; dx++) { block.Motion[list * 4 + (y + dy) * 2 + x + dx] = value; } }
                    }
                    block.SubModes |= (uint)mode << ((y * 2 + x) * 8);
                }
            }
            block.Mode = (byte)mode;
        }

        private void ReadReferences(ref Vp9BooleanReader bits, ref Vp9BlockInfo block, Vp9BlockInfo above, Vp9BlockInfo left, bool hasAbove, bool hasLeft)
        {
            if (header.Segmentation && state.SegmentEnabled[block.Segment * 4 + 2])
            { block.ReferenceZero = (byte)state.SegmentFeatures[block.Segment * 4 + 2]; return; }
            bool compound = compressed.ReferenceMode == 1;
            if (compressed.ReferenceMode == 2)
            { compound = symbols.Read(ref bits, probabilities, Vp9ProbabilityOffset.CompoundMode + Vp9ReferenceContext.CompoundMode(above, left, hasAbove, hasLeft, compressed.FixedReference)); }
            if (compound)
            {
                int variableIndex = 1 - header.SignBias[compressed.FixedReference];
                int context = Vp9ReferenceContext.CompoundReference(above, left, hasAbove, hasLeft, variableIndex, compressed.FixedReference, compressed.VariableReferenceZero, compressed.VariableReferenceOne);
                int variable = symbols.Read(ref bits, probabilities, Vp9ProbabilityOffset.CompoundReference + context) ? compressed.VariableReferenceOne : compressed.VariableReferenceZero;
                block.ReferenceZero = (byte)(variableIndex == 0 ? variable : compressed.FixedReference);
                block.ReferenceOne = (byte)(variableIndex == 1 ? variable : compressed.FixedReference);
                return;
            }
            int reference = 1;
            if (symbols.Read(ref bits, probabilities, Vp9ProbabilityOffset.SingleReference + Vp9ReferenceContext.SingleLast(above, left, hasAbove, hasLeft) * 2))
            { reference = symbols.Read(ref bits, probabilities, Vp9ProbabilityOffset.SingleReference + Vp9ReferenceContext.SingleGolden(above, left, hasAbove, hasLeft) * 2 + 1) ? 3 : 2; }
            block.ReferenceZero = (byte)reference;
        }

        private int ReadInterMode(ref Vp9BooleanReader bits, int context) => 10 + symbols.Tree(ref bits, InterTree, probabilities, Vp9ProbabilityOffset.InterMode + context * 3);

        private static (Vp9MotionVector Nearest, Vp9MotionVector Near) SubMotion(Vp9MotionPrediction prediction, Vp9BlockInfo block, int list, int sub)
        {
            (Vp9MotionVector first, Vp9MotionVector second, _) = prediction.Find(block.Reference(list), sub);
            if (sub == 0) { return (first, second); }
            Vp9MotionVector nearest = block.Vector(list, sub == 3 ? 2 : 0);
            if (sub == 3)
            { for (int i = 1; i >= 0; i--) { Vp9MotionVector value = block.Vector(list, i); if (value != nearest) { return (nearest, value); } } }
            if (first != nearest) { return (nearest, first); }
            if (second != nearest) { return (nearest, second); }
            return (nearest, default);
        }

        private static ReadOnlySpan<sbyte> InterTree => [-2, 2, 0, 4, -1, -3];
        private static ReadOnlySpan<sbyte> FilterTree => [0, 2, -1, -2];
    }
}
