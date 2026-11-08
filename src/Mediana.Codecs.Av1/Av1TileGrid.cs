// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal class Av1TileGrid
    {
        private readonly int[] columns;
        private readonly int[] rows;

        internal Av1TileGrid(ReadOnlySpan<int> columns, ReadOnlySpan<int> rows)
        {
            this.columns = columns.ToArray();
            this.rows = rows.ToArray();
        }

        internal int Columns => columns.Length - 1;
        internal int Rows => rows.Length - 1;

        internal Av1TileBounds Bounds(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Columns * Rows);
            int row = index / Columns;
            int column = index % Columns;
            return new(rows[row], rows[row + 1], columns[column], columns[column + 1]);
        }
    }
}
