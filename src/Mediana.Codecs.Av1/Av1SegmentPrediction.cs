// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1SegmentPrediction
    {
        internal static int Minimum(ReadOnlySpan<byte> map, int columns, int rows, int row, int column, int width, int height)
        {
            if (map.IsEmpty) { return 0; }
            int result = 7;
            for (int y = row; y < Math.Min(row + height, rows); y++)
            {
                for (int x = column; x < Math.Min(column + width, columns); x++) { result = Math.Min(result, map[y * columns + x]); }
            }
            return result;
        }

        internal static int Deinterleave(int difference, int prediction, int maximum)
        {
            if (difference >= maximum) { throw new InvalidDataException("The AV1 segment ID exceeds the active feature range."); }
            if (prediction == 0) { return difference; }
            if (prediction >= maximum - 1) { return maximum - difference - 1; }
            if (difference <= 2 * Math.Min(prediction, maximum - prediction - 1))
            {
                return (difference & 1) != 0 ? prediction + ((difference + 1) >> 1) : prediction - (difference >> 1);
            }
            return 2 * prediction < maximum ? difference : maximum - difference - 1;
        }
    }
}
