// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    internal class PicturePartitions
    {
        private readonly int[] owners;
        private readonly int columns;
        private readonly int log2Ctb;
        private readonly int width;
        private readonly int height;

        public PicturePartitions(IReadOnlyList<IntraSliceHeader> slices)
        {
            Slices = slices;
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
                IntraSliceHeader slice = slices[i];
                int end = (i + 1) < slices.Count ? slices[i + 1].StartCtbAddress : owners.Length;
                if (((i == 0) && (slice.StartCtbAddress != 0)) || (slice.StartCtbAddress >= end) || (end > owners.Length))
                {
                    throw new InvalidDataException("HEVC slices do not cover the picture in coding order.");
                }
                slice.EndCtbAddress = end;
                int rowEntries = ((end - 1) / columns) - (slice.StartCtbAddress / columns);
                if (slice.Picture.EntropyCodingSync && (slice.RowSubstreams.Count != (rowEntries + 1)))
                {
                    throw new InvalidDataException("WPP entry points do not cover every slice row.");
                }
                owners.AsSpan(slice.StartCtbAddress, end - slice.StartCtbAddress).Fill(i);
            }
        }

        public IReadOnlyList<IntraSliceHeader> Slices { get; }

        public IntraSliceHeader SliceAt(int x, int y) => Slices[owners[Address(x, y)]];

        public bool SameSlice(int x, int y, int currentX, int currentY) =>
            (x >= 0) && (y >= 0) && (x < width) && (y < height) && (owners[Address(x, y)] == owners[Address(currentX, currentY)]);

        public bool CanFilter(int x, int y, int otherX, int otherY)
        {
            int first = owners[Address(x, y)];
            int second = owners[Address(otherX, otherY)];
            return (first == second) || Slices[Math.Max(first, second)].LoopFilterAcrossSlices;
        }

        private int Address(int x, int y) => ((y >> log2Ctb) * columns) + (x >> log2Ctb);
    }
}
