// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Hevc
{
    internal class HevcPictureDecoder : IDisposable
    {
        private readonly MediaBufferAllocator allocator;
        private readonly ReconstructionWorkspace workspace;
        private readonly HevcMotionCompensator compensator;
        private bool disposed;

        public HevcPictureDecoder(MediaBufferAllocator allocator)
        {
            this.allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
            workspace = new();
            bool initialized = false;
            try { compensator = new(allocator); initialized = true; }
            finally { if (!initialized) { workspace.Dispose(); } }
        }

        public HevcReferencePicture Decode(IReadOnlyList<HevcSliceHeader> slices, HevcReferenceSet references, int order, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            PicturePartitions partitions = HevcSliceHeaders.Partition(slices);
            HevcSliceHeader first = slices[0];
            SequenceParameters sequence = first.Picture.Sequence;
            DecodedPicture picture = new(sequence, allocator);
            bool completed = false;
            try
            {
                using HevcMotionField motion = new(sequence.Width, sequence.Height, allocator);
                using CodingBlockMap blocks = new(sequence.Width, sequence.Height, partitions);
                SaoFilter sao = new(first, partitions);
                foreach (HevcSliceHeader slice in slices)
                { DecodeSlice(slice, references.CreateLists(slice), picture, blocks, motion, sao, order, cancellationToken); }
                DeblockingFilter.Apply(picture, first, blocks, motion);
                sao.Apply(picture, blocks);
                cancellationToken.ThrowIfCancellationRequested();
                using MediaBuffer published = picture.PublishPixels();
                HevcReferencePicture result = new(picture, order, motion.Compact(allocator));
                completed = true; return result;
            }
            finally { if (!completed) { picture.Dispose(); } }
        }

        private void DecodeSlice(HevcSliceHeader slice, HevcReferenceLists references, DecodedPicture picture,
            CodingBlockMap blocks, HevcMotionField motion, SaoFilter sao, int order, CancellationToken cancellationToken)
        {
            HevcCabacReader reader = new(slice.Reader, slice.Qp, slice.CabacInitializationType);
            QuantizationGroup quantization = new(slice, reader, blocks);
            HevcInterPredictionDecoder prediction = new(slice, reader, picture, blocks, motion, references, order, compensator);
            CodingTreeDecoder tree = new(slice, reader, blocks, quantization, prediction);
            SequenceParameters sequence = slice.Picture.Sequence;
            ResidualDecoder residual = new(reader, slice.Picture.TransformSkip, slice.Picture.SignDataHiding, sequence.ChromaFormat == 3);
            BlockReconstructor reconstruction = new(picture, residual, slice.Picture, quantization, workspace, blocks, blocks.Partitions);
            int ctbSize = 1 << sequence.Log2Ctb, columns = (sequence.Width - 1) / ctbSize + 1;
            int firstRow = slice.StartCtbAddress / columns;
            for (int address = slice.StartCtbAddress; address < slice.EndCtbAddress; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int column = address % columns, row = address / columns;
                if (slice.Picture.EntropyCodingSync && column == 0 && address != slice.StartCtbAddress)
                {
                    reader.StartWavefrontRow(new BitReader(slice.RowSubstreams[row - firstRow]), slice.Qp);
                    quantization.StartWavefrontRow();
                }
                sao.ReadCtb(column, row, reader);
                tree.DecodeCtb(column * ctbSize, row * ctbSize, reconstruction, address + 1 == slice.EndCtbAddress);
            }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            compensator.Dispose(); workspace.Dispose(); disposed = true;
        }
    }
}
