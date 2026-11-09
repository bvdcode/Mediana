// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Codecs.Hevc
{
    /// <summary>
    /// Decodes one independent intra random-access picture using exclusive plane rentals.
    /// </summary>
    internal class IntraPictureDecoder(HevcSliceHeader slice, PicturePartitions? partitions = null)
    {
        private readonly PicturePartitions? regions = partitions ?? (slice.Picture.Tiles.Enabled ? new PicturePartitions([slice]) : null);

        public async Task<DecodedPicture> DecodeAsync(int workerCount, CancellationToken cancellationToken)
        {
            if (slice.Type != HevcSliceType.I)
            {
                throw new UnsupportedHevcFeatureException("inter prediction in an intra-picture decoder");
            }
            if (workerCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "A picture requires at least one decoding worker.");
            }
            SequenceParameters sequence = slice.Picture.Sequence;
            DecodedPicture picture = new DecodedPicture(sequence);
            bool completed = false;
            try
            {
                using CodingBlockMap blocks = new CodingBlockMap(sequence.Width, sequence.Height, regions);
                SaoFilter sao = new SaoFilter(slice, regions);
                await DecodeBlocksAsync(workerCount, picture, blocks, sao, cancellationToken).ConfigureAwait(false);
                if (regions is not null || !slice.DeblockingDisabled)
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
            if (regions is null && slice.Picture.EntropyCodingSync && (workers > 1))
            {
                await new WavefrontDecoder(slice, picture, blocks, sao).DecodeAsync(workers, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (regions is null)
                {
                    DecodeSequential(slice, picture, blocks, sao, cancellationToken);
                }
                else
                {
                    foreach (HevcSliceHeader segment in regions.Slices)
                    {
                        DecodeSequential(segment, picture, blocks, sao, cancellationToken);
                    }
                }
            }
        }

        private void DecodeSequential(HevcSliceHeader slice, DecodedPicture picture, CodingBlockMap blocks, SaoFilter sao, CancellationToken cancellationToken)
        {
            SequenceParameters sequence = slice.Picture.Sequence;
            HevcCabacReader reader = new HevcCabacReader(slice.Reader, slice.Qp);
            QuantizationGroup quantization = new QuantizationGroup(slice, reader, blocks);
            CodingTreeDecoder tree = new CodingTreeDecoder(slice, reader, blocks, quantization);
            ResidualDecoder residual = new ResidualDecoder(reader, slice.Picture.TransformSkip, slice.Picture.SignDataHiding, sequence.ChromaFormat == 3);
            using ReconstructionWorkspace workspace = new ReconstructionWorkspace();
            BlockReconstructor reconstruction = new BlockReconstructor(picture, residual, slice.Picture, quantization, workspace, blocks, regions);
            int ctbSize = 1 << sequence.Log2Ctb;
            HevcTileLayout tiles = slice.Picture.Tiles;
            HevcSubstreamReader substreams = new(slice, reader);
            for (int address = slice.StartCtbAddressTs; address < slice.EndCtbAddressTs; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int raster = tiles.ToRaster(address);
                int column = raster % tiles.Columns;
                int row = raster / tiles.Columns;
                substreams.StartCtb(raster, quantization);
                sao.ReadCtb(column, row, reader);
                tree.DecodeCtb(column * ctbSize, row * ctbSize, reconstruction, (address + 1) == slice.EndCtbAddressTs);
            }
        }
    }
}
