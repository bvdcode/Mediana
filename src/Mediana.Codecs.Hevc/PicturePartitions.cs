// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Hevc
{
    internal class PicturePartitions
    {
        private readonly int[] owners;
        private readonly int columns;
        private readonly int log2Ctb;
        private readonly int width;
        private readonly int height;
        private readonly PictureParameters picture;

        public PicturePartitions(IReadOnlyList<HevcSliceHeader> slices)
        {
            Slices = slices;
            picture = slices[0].Picture;
            SequenceParameters sequence = slices[0].Picture.Sequence;
            width = sequence.Width;
            height = sequence.Height;
            log2Ctb = sequence.Log2Ctb;
            int ctb = 1 << log2Ctb;
            columns = ((width + ctb) - 1) / ctb;
            int rows = ((height + ctb) - 1) / ctb;
            owners = new int[checked(columns * rows)];
            for (int i = 0; i < slices.Count; i++)
            {
                HevcSliceHeader slice = slices[i];
                int start = slice.StartCtbAddressTs;
                int end = (i + 1) < slices.Count ? slices[i + 1].StartCtbAddressTs : owners.Length;
                if (((i == 0) && (start != 0)) || (start >= end) || (end > owners.Length))
                {
                    throw new InvalidDataException("HEVC slices do not cover the picture in coding order.");
                }
                slice.EndCtbAddressTs = end;
                int substreams = 1;
                for (int address = start; address < end; address++)
                {
                    int raster = picture.Tiles.ToRaster(address);
                    owners[raster] = i;
                    if (address != start && picture.Tiles.StartsSubstream(raster, picture.EntropyCodingSync)) { substreams++; }
                }
                if ((picture.Tiles.Enabled || picture.EntropyCodingSync) && slice.EntropySubstreams.Count != substreams)
                {
                    throw new InvalidDataException("HEVC entry points do not cover every tile or wavefront subset in the slice.");
                }
            }
        }

        public IReadOnlyList<HevcSliceHeader> Slices { get; }

        public HevcSliceHeader SliceAt(int x, int y) => Slices[owners[Address(x, y)]];

        public bool SamePredictionRegion(int x, int y, int currentX, int currentY) =>
            (x >= 0) && (y >= 0) && (x < width) && (y < height) &&
            (owners[Address(x, y)] == owners[Address(currentX, currentY)]) &&
            (picture.Tiles.TileId(Address(x, y)) == picture.Tiles.TileId(Address(currentX, currentY)));

        public bool CanFilter(int x, int y, int otherX, int otherY)
        {
            int address = Address(x, y), otherAddress = Address(otherX, otherY);
            if (!picture.LoopFilterAcrossTiles && picture.Tiles.TileId(address) != picture.Tiles.TileId(otherAddress)) { return false; }
            int first = owners[address];
            int second = owners[otherAddress];
            return (first == second) || Slices[Math.Max(first, second)].LoopFilterAcrossSlices;
        }

        private int Address(int x, int y) => ((y >> log2Ctb) * columns) + (x >> log2Ctb);
    }
}
