// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Hevc
{
    internal class TransformTreeDecoder(HevcSliceHeader slice, HevcCabacReader reader,
        CodingBlockMap blocks, QuantizationGroup quantization)
    {
        private readonly SequenceParameters sequence = slice.Picture.Sequence;
        private bool intra, forceInterSplit;

        public void Decode(int x, int y, int log2Size, bool intraPrediction, bool intraSplit,
            bool partitionedInter, ReadOnlySpan<int> chromaModes, bool bypass, BlockReconstructor reconstruction)
        {
            intra = intraPrediction;
            forceInterSplit = !intra && sequence.MaxTransformDepthInter == 0 && partitionedInter;
            int maxDepth = intra ? sequence.MaxTransformDepthIntra + (intraSplit ? 1 : 0) : sequence.MaxTransformDepthInter;
            TransformTree(x, y, x, y, log2Size, 0, 0, intraSplit, chromaModes, 0, 0, bypass, maxDepth, reconstruction);
        }

        private int ReadChromaFlags(int parentFlags, int depth, bool twoBlocks)
        {
            if ((depth != 0) && ((parentFlags & 1) == 0))
            {
                return 0;
            }
            int flags = reader.Read(CabacElement.ChromaCbf, depth) ? 1 : 0;
            if (twoBlocks && reader.Read(CabacElement.ChromaCbf, depth))
            {
                flags |= 2;
            }
            return flags;
        }

        private void TransformTree(int x, int y, int baseX, int baseY, int log2Size, int depth, int index,
            bool intraSplit, ReadOnlySpan<int> chromaModes, int parentCb, int parentCr, bool bypass, int maxDepth, BlockReconstructor reconstruction)
        {
            bool split = ReadTransformSplit(log2Size, depth, maxDepth, intraSplit);

            int cb = parentCb;
            int cr = parentCr;
            if ((sequence.ChromaFormat != 0) && ((log2Size > 2) || (sequence.ChromaFormat == 3)))
            {
                bool twoBlocks = (sequence.ChromaFormat == 2) && (!split || (log2Size == 3));
                cb = ReadChromaFlags(parentCb, depth, twoBlocks);
                cr = ReadChromaFlags(parentCr, depth, twoBlocks);
            }

            if (split)
            {
                int half = 1 << (log2Size - 1);
                for (int child = 0; child < 4; child++)
                {
                    TransformTree(x + ((child & 1) * half), y + ((child >> 1) * half), x, y, log2Size - 1, depth + 1,
                        child, intraSplit, chromaModes.Length == 4 ? chromaModes.Slice(child, 1) : chromaModes,
                        cb, cr, bypass, maxDepth, reconstruction);
                }
            }
            else
            {
                ReconstructTransform(x, y, baseX, baseY, log2Size, depth, index, chromaModes, cb, cr, bypass, reconstruction);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ReadTransformSplit(int log2Size, int depth, int maxDepth, bool intraSplit)
        {
            bool split = (log2Size > sequence.Log2MaxTransform) || ((intraSplit || forceInterSplit) && (depth == 0));
            if (!split && (log2Size > sequence.Log2MinTransform) && (depth < maxDepth))
            {
                split = reader.Read(CabacElement.SplitTransform, 5 - log2Size);
            }
            return split;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReconstructTransform(int x, int y, int baseX, int baseY, int log2Size, int depth, int index,
            ReadOnlySpan<int> chromaModes, int cb, int cr, bool bypass, BlockReconstructor reconstruction)
        {
            bool luma = (!intra && depth == 0 && cb == 0 && cr == 0) || reader.Read(CabacElement.LumaCbf, depth == 0 ? 1 : 0);
            if (luma || (cb != 0) || (cr != 0))
            {
                quantization.ReadDelta();
            }
            blocks.MarkTransform(x, y, 1 << log2Size);
            if (luma) { blocks.MarkLumaResidual(x, y, 1 << log2Size); }
            reconstruction.Reconstruct(x, y, log2Size, 0, blocks.Mode(x, y), luma, bypass, intra);
            if ((sequence.ChromaFormat != 0) && ((sequence.ChromaFormat == 3) || (log2Size > 2) || (index == 3)))
            {
                ReconstructChroma(x, y, baseX, baseY, log2Size, chromaModes[0], cb, cr, bypass, reconstruction);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReconstructChroma(int x, int y, int baseX, int baseY, int log2Size, int chromaMode,
            int cb, int cr, bool bypass, BlockReconstructor reconstruction)
        {
            bool sharedChroma = (sequence.ChromaFormat != 3) && (log2Size == 2);
            int chromaX = (sharedChroma ? baseX : x) / sequence.SubWidthC;
            int chromaY = (sharedChroma ? baseY : y) / sequence.SubHeightC;
            int log2Chroma = Math.Max(2, log2Size - (sequence.ChromaFormat == 3 ? 0 : 1));
            int blockCount = sequence.ChromaFormat == 2 ? 2 : 1;
            for (int component = 1; component <= 2; component++)
            {
                int flags = component == 1 ? cb : cr;
                for (int block = 0; block < blockCount; block++)
                {
                    reconstruction.Reconstruct(chromaX, chromaY + (block << log2Chroma), log2Chroma,
                        component, chromaMode, (flags & (1 << block)) != 0, bypass, intra);
                }
            }
        }
    }
}
