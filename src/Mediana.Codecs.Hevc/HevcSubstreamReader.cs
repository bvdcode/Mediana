// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcSubstreamReader(HevcSliceHeader slice, HevcCabacReader reader)
    {
        private int nextSubstream = 1;

        public void StartCtb(int rasterAddress, QuantizationGroup quantization)
        {
            HevcTileLayout tiles = slice.Picture.Tiles;
            if (rasterAddress == slice.StartCtbAddress || !tiles.StartsSubstream(rasterAddress, slice.Picture.EntropyCodingSync))
            {
                return;
            }
            if (nextSubstream >= slice.EntropySubstreams.Count)
            {
                throw new InvalidDataException("Missing HEVC tile or wavefront entry point.");
            }
            BitReader stream = new(slice.EntropySubstreams[nextSubstream++]);
            if (tiles.StartsTile(rasterAddress))
            {
                reader.StartTile(stream, slice.Qp);
            }
            else
            {
                reader.StartWavefrontRow(stream, slice.Qp);
            }
            quantization.ResetPredictor();
        }
    }
}
