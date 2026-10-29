// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    internal class QuantizationGroup(IntraSliceHeader slice, IntraCabacReader reader, CodingBlockMap blocks)
    {
        private int previousQp = slice.Qp;
        private int currentQp = slice.Qp;
        private bool deltaCoded;

        public void StartWavefrontRow()
        {
            previousQp = slice.Qp;
            currentQp = slice.Qp;
        }

        public void Begin(int x, int y, int log2Size)
        {
            PictureParameters pps = slice.Picture;
            SequenceParameters sps = pps.Sequence;
            if (!pps.CuQpDelta || (log2Size < (sps.Log2Ctb - pps.QpDeltaDepth)))
            {
                return;
            }
            int left = NeighbourQp(x - 1, y, x, y);
            int top = NeighbourQp(x, y - 1, x, y);
            currentQp = (left + top + 1) >> 1;
            deltaCoded = false;
        }

        public void ReadDelta()
        {
            if (!slice.Picture.CuQpDelta || deltaCoded)
            {
                return;
            }
            int offset = 6 * (slice.Picture.Sequence.BitDepth - 8);
            int delta = reader.QpDelta();
            if ((delta < (-26 - (offset / 2))) || (delta > (25 + (offset / 2))))
            {
                throw new InvalidDataException("Invalid luma QP delta.");
            }
            currentQp = ((currentQp + delta + 52 + (2 * offset)) % (52 + offset)) - offset;
            deltaCoded = true;
        }

        public int ForComponent(int component)
        {
            int depthOffset = 6 * (slice.Picture.Sequence.BitDepth - 8);
            if (component == 0)
            {
                return currentQp + depthOffset;
            }
            int colourOffset;
            switch (component)
            {
                case 1:
                    colourOffset = slice.CbQpOffset + slice.Picture.CbQpOffset;
                    break;
                case 2:
                    colourOffset = slice.CrQpOffset + slice.Picture.CrQpOffset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(component));
            }

            int qp = Math.Min(57, Math.Max(-depthOffset, currentQp + colourOffset));
            return QuantizationMath.Chroma(qp, slice.Picture.Sequence.ChromaFormat) + depthOffset;
        }

        public void CompleteCodingUnit(int x, int y, int size)
        {
            blocks.SetQp(x, y, size, size, currentQp);
            previousQp = currentQp;
        }

        private int NeighbourQp(int x, int y, int currentX, int currentY)
        {
            int log2Ctb = slice.Picture.Sequence.Log2Ctb;
            if ((x < 0) || (y < 0) || ((x >> log2Ctb) != (currentX >> log2Ctb)) || ((y >> log2Ctb) != (currentY >> log2Ctb)))
            {
                return previousQp;
            }

            int qp = blocks.Depth(x, y) < 0 ? int.MinValue : blocks.Qp(x, y);
            return qp == int.MinValue ? previousQp : qp;
        }
    }
}
