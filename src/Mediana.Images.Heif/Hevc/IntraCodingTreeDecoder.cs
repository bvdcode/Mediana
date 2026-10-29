// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Mediana.Images.Heif.Hevc
{
    internal class IntraCodingTreeDecoder(IntraSliceHeader slice, IntraCabacReader reader,
        CodingBlockMap blocks, QuantizationGroup quantization)
    {
        private readonly SequenceParameters sequence = slice.Picture.Sequence;
        private readonly IntraModeDecoder modes = new IntraModeDecoder(reader, blocks, slice.Picture.Sequence.Log2Ctb);

        public void DecodeCtb(int x, int y, BlockReconstructor reconstruction, bool endsSlice = false)
        {
            int ctbSize = 1 << sequence.Log2Ctb;
            CodingTree(x, y, sequence.Log2Ctb, 0, reconstruction);
            if (slice.Picture.EntropyCodingSync && (x == ctbSize))
            {
                reader.StoreWavefrontContexts();
            }
            bool last = endsSlice || (((x + ctbSize) >= sequence.Width) && ((y + ctbSize) >= sequence.Height));
            if (reader.Termination() != last)
            {
                throw new InvalidDataException("Unexpected HEVC slice termination.");
            }
            if (slice.Picture.EntropyCodingSync && !last && ((x + ctbSize) >= sequence.Width) && !reader.Termination())
            {
                throw new InvalidDataException("Missing HEVC row termination.");
            }
        }

        private void CodingTree(int x, int y, int log2Size, int depth, BlockReconstructor reconstruction)
        {
            int size = 1 << log2Size;
            bool split = ReadCodingSplit(x, y, log2Size, depth);

            quantization.Begin(x, y, log2Size);
            if (split)
            {
                int half = size / 2;
                for (int i = 0; i < 4; i++)
                {
                    int cx = x + ((i & 1) * half);
                    int cy = y + ((i >> 1) * half);
                    if ((cx < sequence.Width) && (cy < sequence.Height))
                    {
                        CodingTree(cx, cy, log2Size - 1, depth + 1, reconstruction);
                    }
                }
            }
            else
            {
                DecodeCodingUnit(x, y, log2Size, depth, reconstruction);
            }
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
            bool split = (log2Size > sequence.Log2MaxTransform) || (intraSplit && (depth == 0));
            if (!split && (log2Size > sequence.Log2MinTransform) && (depth < maxDepth))
            {
                split = reader.Read(CabacElement.SplitTransform, 5 - log2Size);
            }
            return split;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ReadCodingSplit(int x, int y, int log2Size, int depth)
        {
            int size = 1 << log2Size;
            bool split = ((x + size) > sequence.Width) || ((y + size) > sequence.Height);
            if (!split && (log2Size > sequence.Log2MinCodingBlock))
            {
                bool left = blocks.Partitions is null || blocks.Partitions.SameSlice(x - 1, y, x, y);
                bool top = blocks.Partitions is null || blocks.Partitions.SameSlice(x, y - 1, x, y);
                int context = (left && (blocks.Depth(x - 1, y) > depth) ? 1 : 0) +
                    (top && (blocks.Depth(x, y - 1) > depth) ? 1 : 0);
                split = reader.Read(CabacElement.SplitCodingUnit, context);
            }

            return split;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DecodeCodingUnit(int x, int y, int log2Size, int depth, BlockReconstructor reconstruction)
        {
            int size = 1 << log2Size;
            bool bypass = slice.Picture.TransquantBypass && reader.Read(CabacElement.TransquantBypass);
            bool intraSplit = (log2Size == sequence.Log2MinCodingBlock) && !reader.Read(CabacElement.PartMode);
            blocks.SetCodingUnit(x, y, size, depth, bypass);
            modes.ReadLuma(x, y, size, intraSplit);
            Span<int> chromaModes = stackalloc int[4];
            int modeCount = intraSplit && (sequence.ChromaFormat == 3) ? 4 : 1;
            for (int part = 0; part < modeCount; part++)
            {
                int px = x + ((part & 1) * (size / 2));
                int py = y + ((part >> 1) * (size / 2));
                chromaModes[part] = sequence.ChromaFormat == 0 ? 0 : modes.ReadChroma(blocks.Mode(px, py), sequence.ChromaFormat);
            }
            TransformTree(x, y, x, y, log2Size, 0, 0, intraSplit, chromaModes.Slice(0, modeCount), 0, 0, bypass,
                sequence.MaxTransformDepthIntra + (intraSplit ? 1 : 0), reconstruction);
            quantization.CompleteCodingUnit(x, y, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReconstructTransform(int x, int y, int baseX, int baseY, int log2Size, int depth, int index,
            ReadOnlySpan<int> chromaModes, int cb, int cr, bool bypass, BlockReconstructor reconstruction)
        {
            bool luma = reader.Read(CabacElement.LumaCbf, depth == 0 ? 1 : 0);
            if (luma || (cb != 0) || (cr != 0))
            {
                quantization.ReadDelta();
            }
            blocks.MarkTransform(x, y, 1 << log2Size);
            reconstruction.Reconstruct(x, y, log2Size, 0, blocks.Mode(x, y), luma, bypass);
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
                        component, chromaMode, (flags & (1 << block)) != 0, bypass);
                }
            }
        }
    }
}
