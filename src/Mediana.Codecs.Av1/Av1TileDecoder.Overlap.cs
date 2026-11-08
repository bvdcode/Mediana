// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private const int MaximumOverlapSamples = 32 * 64;

        private void PredictOverlap(int row, int column, int width, int height, bool hasChroma, Av1ReferencePictures pictures)
        {
            Span<byte> prediction = stackalloc byte[MaximumOverlapSamples];
            for (int plane = 0; plane < (hasChroma ? 3 : 1); plane++)
            {
                int sub = sequence.PlaneShift(plane);
                if (row > bounds.RowStart && BlockIndex(Math.Max(1, width >> sub), Math.Max(1, height >> sub)) >= 3)
                {
                    PredictOverlapSide(row, column, width, height, plane, 0, pictures, prediction);
                }
                if (column > bounds.ColumnStart) { PredictOverlapSide(row, column, width, height, plane, 1, pictures, prediction); }
            }
        }

        private void PredictOverlapSide(int row, int column, int width, int height, int plane, int pass,
            Av1ReferencePictures pictures, scoped Span<byte> prediction)
        {
            int sub = sequence.PlaneShift(plane);
            int w = Math.Max(4, (width * 4) >> sub);
            int h = Math.Max(4, (height * 4) >> sub);
            int coordinate = pass == 0 ? column : row;
            int end = pass == 0 ? Math.Min(sequence.MiColumns, column + width) : Math.Min(sequence.MiRows, row + height);
            int limit = Math.Min(4, BitOperations.Log2((uint)(pass == 0 ? width : height)));
            int neighbors = 0;
            while (coordinate < end && neighbors < limit)
            {
                int candidateRow = pass == 0 ? row - 1 : coordinate | 1;
                int candidateColumn = pass == 0 ? coordinate | 1 : column - 1;
                Av1MotionBlock candidate = motionBlocks[candidateRow * sequence.MiColumns + candidateColumn];
                int extent = pass == 0 ? candidate.Width : candidate.Height;
                if (extent == 0) { throw new InvalidDataException("The AV1 overlap motion block is unavailable."); }
                int step = Math.Clamp(extent, 2, 16);
                if (candidate.Reference != 0)
                {
                    neighbors++;
                    int predW = pass == 0 ? Math.Min(w, (step * 4) >> sub) : Math.Min(w >> 1, 32 >> sub);
                    int predH = pass == 0 ? Math.Min(h >> 1, 32 >> sub) : Math.Min(h, (step * 4) >> sub);
                    int x = ((pass == 0 ? coordinate : column) * 4) >> sub;
                    int y = ((pass == 0 ? row : coordinate) * 4) >> sub;
                    Span<byte> region = prediction[..(predW * predH)];
                    Av1StoredPicture reference = pictures.Get(header.Inter.References[candidate.Reference - 1]);
                    Av1MotionCompensation.Predict(region, sequence, reference, plane, x, y, predW, predH, candidate);
                    BlendOverlap(region, plane, x, y, predW, predH, pass);
                }
                coordinate += step;
            }
        }

        private void BlendOverlap(scoped ReadOnlySpan<byte> prediction, int plane, int x, int y, int width, int height, int pass)
        {
            ReadOnlySpan<byte> mask = Av1ObmcMask.ForLength(pass == 0 ? height : width);
            int stride = sequence.PlaneStride(plane);
            int clippedWidth = Math.Min(width, stride - x);
            int clippedHeight = Math.Min(height, sequence.PlaneHeight(plane) - y);
            int offset = sequence.PlaneOffset(plane);
            for (int row = 0; row < clippedHeight; row++)
            {
                Span<byte> destination = output.Slice(offset + (y + row) * stride + x, clippedWidth);
                for (int column = 0; column < clippedWidth; column++)
                {
                    int weight = mask[pass == 0 ? row : column];
                    destination[column] = (byte)((weight * destination[column] + (64 - weight) * prediction[row * width + column] + 32) >> 6);
                }
            }
        }
    }
}
