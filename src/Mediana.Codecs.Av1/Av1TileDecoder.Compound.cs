// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private Av1CompoundPrediction ReadCompoundPrediction(int row, int column, int width, int height, ref Av1MotionBlock block, Av1ReferencePictures pictures)
        {
            if (block.SkipMode) { return new(Av1CompoundWeights.Average); }
            Av1MotionBlock top = row > bounds.RowStart ? motionBlocks[(row - 1) * sequence.MiColumns + column] : default;
            Av1MotionBlock prior = column > bounds.ColumnStart ? motionBlocks[row * sequence.MiColumns + column - 1] : default;
            if (sequence.Tools.MaskedCompound)
            {
                int context = Math.Min(5, CompoundGroupContext(top) + CompoundGroupContext(prior));
                block.CompoundGroup = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CompGroupOffset + context * 3, 3));
                if (block.CompoundGroup != 0)
                {
                    int size = BlockIndex(width, height);
                    bool wedge = Av1WedgeMask.IsSupported(size) && symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CompoundTypeOffset + size * 3, 3)) == 0;
                    int index = wedge ? symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.WedgeIndexOffset + size * 17, 17)) : 0;
                    return new(default, wedge ? Av1CompoundMaskType.Wedge : Av1CompoundMaskType.Difference, index, (int)symbols.ReadLiteral(1));
                }
            }
            if (!sequence.Tools.JointCompound) { return new(Av1CompoundWeights.Average); }
            int firstHint = pictures.OrderHint(header.Inter.References[block.Reference - 1]);
            int secondHint = pictures.OrderHint(header.Inter.References[block.SecondReference - 1]);
            int first = Av1ReferenceOrdering.Distance(firstHint, header.OrderHint, sequence.Tools.OrderHintBits);
            int second = Av1ReferenceOrdering.Distance(secondHint, header.OrderHint, sequence.Tools.OrderHintBits);
            int indexContext = (Math.Abs(first) == Math.Abs(second) ? 3 : 0) + CompoundIndexContext(top) + CompoundIndexContext(prior);
            block.CompoundIndex = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CompoundIndexOffset + indexContext * 3, 3));
            return new(block.CompoundIndex == 0 ? Av1CompoundWeights.FromDistances(first, second) : Av1CompoundWeights.Average);
        }

        private static int CompoundGroupContext(Av1MotionBlock block) => block.SecondReference != 0 ? block.CompoundGroup : block.Reference == 7 ? 3 : 0;

        private static int CompoundIndexContext(Av1MotionBlock block) => block.SecondReference != 0 ? block.CompoundIndex : block.Reference == 7 ? 1 : 0;
    }
}
