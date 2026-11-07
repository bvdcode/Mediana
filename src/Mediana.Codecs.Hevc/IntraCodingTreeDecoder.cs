// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Hevc
{
    internal class IntraCodingTreeDecoder(HevcSliceHeader slice, HevcCabacReader reader,
        CodingBlockMap blocks, QuantizationGroup quantization)
    {
        private readonly SequenceParameters sequence = slice.Picture.Sequence;
        private readonly TransformTreeDecoder transforms = new(slice, reader, blocks, quantization);
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
            transforms.Decode(x, y, log2Size, true, intraSplit, false, chromaModes[..modeCount], bypass, reconstruction);
            quantization.CompleteCodingUnit(x, y, size);
        }

    }
}
