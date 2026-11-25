// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264CabacSpatialSkipRun
    {
        private readonly H264Picture target;
        private readonly H264SliceHeader header;
        private readonly H264BidirectionalReferences references;
        private readonly H264PictureOrder order;
        private readonly int columns;
        private readonly ReadOnlySpan<byte> indicesL0;
        private readonly ReadOnlySpan<byte> indicesL1;
        private readonly ReadOnlySpan<H264MacroblockFlags> flags;

        public H264CabacSpatialSkipRun(H264Picture target, H264SliceHeader header,
            H264BidirectionalReferences references, H264PictureOrder order)
        {
            this.target = target;
            this.header = header;
            this.references = references;
            this.order = order;
            columns = target.Sequence.WidthInMbs;
            if (header.Kind != H264SliceKind.B || !header.DirectSpatialMotionPrediction || target.SampleBytes != 1 ||
                target.Structure != H264PictureStructure.Frame || target.Layout.MacroblockAdaptiveFrameField ||
                header.Picture.WeightedBiPrediction is not (0 or 2) || references.L1.Count == 0 ||
                references.L1[0] is not H264ReferencePicture colocated)
            {
                return;
            }
            H264ColocatedPicture picture = new(colocated, target, new(H264PictureStructure.Frame, 0, 0, false), order);
            if (picture.SharesFrameGrid)
            {
                indicesL0 = target.ReferenceIndices(H264ReferenceList.L0);
                indicesL1 = target.ReferenceIndices(H264ReferenceList.L1);
                flags = target.MacroblockFlags;
            }
        }

        public bool Enabled => !flags.IsEmpty;

        public bool TryRead(ref H264BitReader bits, H264CabacReader reader, scoped Span<byte> types,
            scoped Span<byte> quantizers, int address, int sliceEnd, int qp, CancellationToken token,
            out int next, out bool nonSkippedFlagRead)
        {
            (int row, int column) = Math.DivRem(address, columns);
            int x = column * 16;
            int y = row * 16;
            int rowEnd = address + Math.Min(sliceEnd - address, columns - column);
            next = address;
            nonSkippedFlagRead = false;
            H264DirectMotion motion = H264MotionPrediction.SpatialDirect(target, x, y);
            if (!H264StationaryBiPrediction.TrySelect(target, header, references, order, motion,
                out H264Picture? first, out H264Picture? second, out int weight))
            {
                return false;
            }
            byte referenceL0 = motion.L0.ReferenceIndex < 0 ? byte.MaxValue : (byte)motion.L0.ReferenceIndex;
            byte referenceL1 = motion.L1.ReferenceIndex < 0 ? byte.MaxValue : (byte)motion.L1.ReferenceIndex;
            int stride = 4 * columns;
            int top = address - columns + 1;
            int topBlock = (4 * row - 1) * stride + 4 * (column + 1);
            next = address + 1;
            while (true)
            {
                reader.ReadSliceBoundary(ref bits, next == sliceEnd);
                if (next == rowEnd || !KeepsPrediction(top, topBlock, column + next - address, referenceL0, referenceL1))
                {
                    break;
                }
                token.ThrowIfCancellationRequested();
                int context = top >= header.FirstMacroblock && types[top] != H264CabacMacroblockState.Skipped ? 1 : 0;
                if (!H264CabacMacroblockSyntax.ReadBidirectionalSkip(ref bits, reader, context))
                {
                    nonSkippedFlagRead = true;
                    break;
                }
                next++;
                top++;
                topBlock += 4;
            }
            int count = next - address;
            types.Slice(address, count).Fill(H264CabacMacroblockState.Skipped);
            quantizers.Slice(address, count).Fill((byte)qp);
            H264StationaryBiPrediction.WriteStrip(target, x, y, count, first, second, weight);
            target.MarkStationarySkip(x, y, count, motion.L0.ReferenceIndex, motion.L1.ReferenceIndex);
            return true;
        }

        private bool KeepsPrediction(int top, int topBlock, int column, byte referenceL0, byte referenceL1)
        {
            if (!PreservesMotion(top, topBlock, referenceL0, referenceL1))
            {
                return false;
            }
            if (column < columns - 1 && top + 1 >= header.FirstMacroblock)
            {
                return PreservesMotion(top + 1, topBlock + 4, referenceL0, referenceL1);
            }
            return PreservesMotion(top - 1, topBlock - 1, referenceL0, referenceL1);
        }

        // With zero motion in all available neighbors, the minimum reference index on each list decides the prediction.
        private bool PreservesMotion(int neighbor, int block, byte referenceL0, byte referenceL1) =>
            neighbor < header.FirstMacroblock || ((flags[neighbor] & H264MacroblockFlags.StationarySkip) != 0 &&
                indicesL0[block] >= referenceL0 && indicesL1[block] >= referenceL1);
    }
}
