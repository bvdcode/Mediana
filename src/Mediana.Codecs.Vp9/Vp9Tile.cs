// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal readonly record struct Vp9Tile(ReadOnlyMemory<byte> Encoded, int RowStart, int RowEnd, int ColumnStart, int ColumnEnd);
}
