// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private Av1CompoundWeights ReadCompoundWeights(int row, int column, ref Av1MotionBlock block, Av1ReferencePictures pictures)
        {
            if (block.SkipMode) { return Av1CompoundWeights.Average; }
            Av1MotionBlock top = row > 0 ? motionBlocks[(row - 1) * sequence.MiColumns + column] : default;
            Av1MotionBlock prior = column > 0 ? motionBlocks[row * sequence.MiColumns + column - 1] : default;
            if (sequence.Tools.MaskedCompound)
            {
                int context = Math.Min(5, CompoundGroupContext(top) + CompoundGroupContext(prior));
                block.CompoundGroup = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CompGroupOffset + context * 3, 3));
                if (block.CompoundGroup != 0) { throw new NotSupportedException("AV1 masked compound reconstruction is not implemented."); }
            }
            if (!sequence.Tools.JointCompound) { return Av1CompoundWeights.Average; }
            int firstHint = pictures.OrderHint(header.Inter.References[block.Reference - 1]);
            int secondHint = pictures.OrderHint(header.Inter.References[block.SecondReference - 1]);
            int first = Av1ReferenceOrdering.Distance(firstHint, header.OrderHint, sequence.Tools.OrderHintBits);
            int second = Av1ReferenceOrdering.Distance(secondHint, header.OrderHint, sequence.Tools.OrderHintBits);
            int indexContext = (Math.Abs(first) == Math.Abs(second) ? 3 : 0) + CompoundIndexContext(top) + CompoundIndexContext(prior);
            block.CompoundIndex = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CompoundIndexOffset + indexContext * 3, 3));
            return block.CompoundIndex == 0 ? Av1CompoundWeights.FromDistances(first, second) : Av1CompoundWeights.Average;
        }

        private static int CompoundGroupContext(Av1MotionBlock block) => block.SecondReference != 0 ? block.CompoundGroup : block.Reference == 7 ? 3 : 0;

        private static int CompoundIndexContext(Av1MotionBlock block) => block.SecondReference != 0 ? block.CompoundIndex : block.Reference == 7 ? 1 : 0;
    }
}
