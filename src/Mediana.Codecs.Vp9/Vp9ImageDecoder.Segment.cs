// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9
{
    internal ref partial struct Vp9ImageDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private int Segment(ref Vp9BooleanReader bits, int row, int column, int size)
        {
            if (!header.Segmentation) { return 0; }
            if (header.IsIntra) { return header.UpdateSegmentMap ? bits.Tree(SegmentTree, header.SegmentTree) : 0; }
            int predicted = 0;
            if (!header.ErrorResilient && !previous.IsEmpty)
            {
                predicted = 7;
                for (int y = row; y < Math.Min(header.MiRows, row + Math.Max(1, Heights[size] / 8)); y++)
                {
                    for (int x = column; x < Math.Min(header.MiColumns, column + Math.Max(1, Widths[size] / 8)); x++)
                    { predicted = Math.Min(predicted, previous[y * miStride + x].Segment); }
                }
            }
            if (!header.UpdateSegmentMap) { return predicted; }
            if (!header.TemporalSegments) { return bits.Tree(SegmentTree, header.SegmentTree); }
            bool reuse = bits.Read(header.SegmentPrediction[aboveSegment[column] + leftSegment[row & 7]]);
            aboveSegment.Slice(column, Math.Max(1, Widths[size] / 8)).Fill(reuse ? (byte)1 : (byte)0);
            for (int y = 0; y < Math.Max(1, Heights[size] / 8); y++) { leftSegment[(row + y) & 7] = reuse ? (byte)1 : (byte)0; }
            return reuse ? predicted : bits.Tree(SegmentTree, header.SegmentTree);
        }
    }
}
