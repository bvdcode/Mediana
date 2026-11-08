// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1TileBounds(int RowStart, int RowEnd, int ColumnStart, int ColumnEnd)
    {
        internal bool Contains(int row, int column) => row >= RowStart && row < RowEnd && column >= ColumnStart && column < ColumnEnd;
    }
}
