// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private Av1SegmentFeatures Segment => header.Segmentation.Segments[segmentId];

        private void ReadSegment(int row, int column, int width, int height, bool inter, bool preSkip, bool skip)
        {
            segmentId = 0;
            if (!header.Segmentation.Enabled) { return; }
            if (inter && !header.Segmentation.UpdateMap)
            {
                segmentId = Av1SegmentPrediction.Minimum(previousSegments, sequence.MiColumns, sequence.MiRows, row, column, width, height);
                return;
            }
            if (inter && preSkip && !segmentsBeforeSkip) { return; }
            bool predicted = false;
            if (inter && header.Segmentation.TemporalUpdate && (preSkip || !skip))
            {
                int context = (row > bounds.RowStart && above[column].SegmentPredicted ? 1 : 0) +
                    (column > bounds.ColumnStart && left[row & (LeftContexts - 1)].SegmentPredicted ? 1 : 0);
                predicted = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SegmentPredictedOffset + context * 3, 3)) != 0;
            }
            if (predicted)
            {
                segmentId = Av1SegmentPrediction.Minimum(previousSegments, sequence.MiColumns, sequence.MiRows, row, column, width, height);
            }
            else { segmentId = ReadSpatialSegment(row, column, skip); }
            if (inter && (header.Segmentation.TemporalUpdate || !preSkip && skip))
            {
                for (int x = column; x < Math.Min(column + width, sequence.MiColumns); x++) { above[x].SegmentPredicted = predicted; }
                for (int y = row; y < Math.Min(row + height, sequence.MiRows); y++) { left[y & (LeftContexts - 1)].SegmentPredicted = predicted; }
            }
        }

        private int ReadSpatialSegment(int row, int column, bool skip)
        {
            int topLeft = row > bounds.RowStart && column > bounds.ColumnStart ? segmentMap[(row - 1) * sequence.MiColumns + column - 1] : -1;
            int top = row > bounds.RowStart ? segmentMap[(row - 1) * sequence.MiColumns + column] : -1;
            int prior = column > bounds.ColumnStart ? segmentMap[row * sequence.MiColumns + column - 1] : -1;
            int prediction = top < 0 ? prior < 0 ? 0 : prior : prior < 0 ? top : topLeft == top ? top : prior;
            if (skip) { return prediction; }
            int context = topLeft < 0 ? 0 : topLeft == top && topLeft == prior ? 2 : topLeft == top || topLeft == prior || top == prior ? 1 : 0;
            int difference = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SegmentIdOffset + context * 9, 9));
            return Av1SegmentPrediction.Deinterleave(difference, prediction, lastActiveSegment + 1);
        }

        private void StoreSegment(int row, int column, int width, int height)
        {
            if (!header.Segmentation.Enabled || !header.Segmentation.UpdateMap) { return; }
            for (int y = row; y < Math.Min(row + height, sequence.MiRows); y++)
            {
                segmentMap.Slice(y * sequence.MiColumns + column, Math.Min(width, sequence.MiColumns - column)).Fill((byte)segmentId);
            }
        }
    }
}
