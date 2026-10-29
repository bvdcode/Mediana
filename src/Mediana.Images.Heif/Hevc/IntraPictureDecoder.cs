// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Decodes one independent intra random-access picture using exclusive plane rentals.
    /// </summary>
    internal class IntraPictureDecoder(IntraSliceHeader slice, PicturePartitions? partitions = null)
    {
        public async Task<DecodedPicture> DecodeAsync(int workerCount, CancellationToken cancellationToken)
        {
            if (workerCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "A picture requires at least one decoding worker.");
            }
            SequenceParameters sequence = slice.Picture.Sequence;
            DecodedPicture picture = new DecodedPicture(sequence);
            bool completed = false;
            try
            {
                using CodingBlockMap blocks = new CodingBlockMap(sequence.Width, sequence.Height, partitions);
                SaoFilter sao = new SaoFilter(slice, partitions);
                await DecodeBlocksAsync(workerCount, picture, blocks, sao, cancellationToken).ConfigureAwait(false);
                if (partitions is not null || !slice.DeblockingDisabled)
                {
                    DeblockingFilter.Apply(picture, slice, blocks);
                }
                sao.Apply(picture, blocks);
                cancellationToken.ThrowIfCancellationRequested();
                completed = true;
                return picture;
            }
            finally
            {
                if (!completed)
                {
                    picture.Dispose();
                }
            }
        }

        private async Task DecodeBlocksAsync(int workerCount, DecodedPicture picture, CodingBlockMap blocks, SaoFilter sao, CancellationToken cancellationToken)
        {
            SequenceParameters sequence = slice.Picture.Sequence;
            int ctbSize = 1 << sequence.Log2Ctb;
            int columns = ((sequence.Width + ctbSize) - 1) / ctbSize;
            int rows = ((sequence.Height + ctbSize) - 1) / ctbSize;
            int workers = Math.Min(workerCount, Math.Min(rows, (columns + 1) / 2));
            if (partitions is null && slice.Picture.EntropyCodingSync && (workers > 1))
            {
                await new WavefrontDecoder(slice, picture, blocks, sao).DecodeAsync(workers, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (partitions is null)
                {
                    DecodeSequential(slice, picture, blocks, sao, cancellationToken);
                }
                else
                {
                    foreach (IntraSliceHeader segment in partitions.Slices)
                    {
                        DecodeSequential(segment, picture, blocks, sao, cancellationToken);
                    }
                }
            }
        }

        private void DecodeSequential(IntraSliceHeader slice, DecodedPicture picture, CodingBlockMap blocks, SaoFilter sao, CancellationToken cancellationToken)
        {
            SequenceParameters sequence = slice.Picture.Sequence;
            IntraCabacReader reader = new IntraCabacReader(slice.Reader, slice.Qp);
            QuantizationGroup quantization = new QuantizationGroup(slice, reader, blocks);
            IntraCodingTreeDecoder tree = new IntraCodingTreeDecoder(slice, reader, blocks, quantization);
            ResidualDecoder residual = new ResidualDecoder(reader, slice.Picture.TransformSkip, slice.Picture.SignDataHiding, sequence.ChromaFormat == 3);
            using ReconstructionWorkspace workspace = new ReconstructionWorkspace();
            BlockReconstructor reconstruction = new BlockReconstructor(picture, residual, slice.Picture, quantization, workspace, partitions);
            int ctbSize = 1 << sequence.Log2Ctb;
            int columns = ((sequence.Width + ctbSize) - 1) / ctbSize;
            int firstRow = slice.StartCtbAddress / columns;
            for (int address = slice.StartCtbAddress; address < slice.EndCtbAddress; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int column = address % columns;
                int row = address / columns;
                if (slice.Picture.EntropyCodingSync && (column == 0) && (address != slice.StartCtbAddress))
                {
                    reader.StartWavefrontRow(new BitReader(slice.RowSubstreams[row - firstRow]), slice.Qp);
                    quantization.StartWavefrontRow();
                }
                sao.ReadCtb(column, row, reader);
                tree.DecodeCtb(column * ctbSize, row * ctbSize, reconstruction, (address + 1) == slice.EndCtbAddress);
            }
        }
    }
}
