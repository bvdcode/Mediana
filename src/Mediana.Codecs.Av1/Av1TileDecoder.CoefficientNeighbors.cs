// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private static int CoefficientBaseContext(scoped ReadOnlySpan<int> levels, int position, int transformClass, Av1TransformSize size)
        {
            if (position == 0 && transformClass == 0)
            {
                return 0;
            }
            int row = position / size.CodedWidth;
            int column = position % size.CodedWidth;
            if (transformClass != 0)
            {
                int extent = transformClass == 1 ? column : row;
                int sum = Neighbor(levels, row, column + 1, 3, size) + Neighbor(levels, row + 1, column, 3, size);
                for (int step = 2; step <= 4; step++)
                {
                    sum += transformClass == 1 ? Neighbor(levels, row, column + step, 3, size) : Neighbor(levels, row + step, column, 3, size);
                }
                return Math.Min((sum + 1) >> 1, 4) + 26 + Math.Min(extent, 2) * 5;
            }
            int magnitude = Neighbor(levels, row, column + 1, 3, size) + Neighbor(levels, row + 1, column, 3, size) +
                Neighbor(levels, row + 1, column + 1, 3, size) + Neighbor(levels, row, column + 2, 3, size) +
                Neighbor(levels, row + 2, column, 3, size);
            return Math.Min((magnitude + 1) >> 1, 4) + Av1CoefficientTables.BaseOffset(size.Index, row, column);
        }

        private static int CoefficientRangeContext(scoped ReadOnlySpan<int> levels, int position, int transformClass, Av1TransformSize size)
        {
            int row = position / size.CodedWidth;
            int column = position % size.CodedWidth;
            int magnitude = Neighbor(levels, row, column + 1, 15, size) + Neighbor(levels, row + 1, column, 15, size) +
                (transformClass switch
                {
                    0 => Neighbor(levels, row + 1, column + 1, 15, size),
                    1 => Neighbor(levels, row, column + 2, 15, size),
                    2 => Neighbor(levels, row + 2, column, 15, size),
                    _ => throw new InvalidDataException("The AV1 transform class is invalid.")
                });
            int context = Math.Min((magnitude + 1) >> 1, 6);
            if (position == 0)
            {
                return context;
            }
            bool near = transformClass switch
            {
                0 => row < 2 && column < 2,
                1 => column == 0,
                2 => row == 0,
                _ => throw new InvalidDataException("The AV1 transform class is invalid.")
            };
            return context + (near ? 7 : 14);
        }

        private static int Neighbor(scoped ReadOnlySpan<int> levels, int row, int column, int maximum, Av1TransformSize size)
        {
            return row < size.CodedHeight && column < size.CodedWidth ? Math.Min(levels[row * size.CodedWidth + column], maximum) : 0;
        }
    }
}
