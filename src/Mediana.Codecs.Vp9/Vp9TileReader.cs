// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Vp9
{
    internal struct Vp9TileReader(ReadOnlyMemory<byte> encoded, Vp9FrameHeader header)
    {
        private int position, index;
        internal Vp9Tile Current { get; private set; }

        internal bool MoveNext()
        {
            int columns = 1 << header.TileColumnsLog2, rows = 1 << header.TileRowsLog2;
            if (index == columns * rows) { return false; }
            int size = encoded.Length - position;
            if (index != columns * rows - 1)
            {
                if (size < 4) { throw new InvalidDataException("Truncated VP9 tile size."); }
                uint declared = BinaryPrimitives.ReadUInt32BigEndian(encoded.Span[position..]); position += 4;
                if (declared > encoded.Length - position) { throw new InvalidDataException("A VP9 tile exceeds its frame."); }
                size = (int)declared;
            }
            if (size == 0) { throw new InvalidDataException("A VP9 arithmetic tile must not be empty."); }
            int row = index >> header.TileColumnsLog2, column = index & (columns - 1);
            Current = new(encoded.Slice(position, size), Offset(row, header.MiRows, header.TileRowsLog2),
                Offset(row + 1, header.MiRows, header.TileRowsLog2), Offset(column, header.MiColumns, header.TileColumnsLog2),
                Offset(column + 1, header.MiColumns, header.TileColumnsLog2));
            position += size; index++; return true;
        }

        private static int Offset(int tile, int extent, int shift) => Math.Min(extent, ((tile * ((extent + 7) >> 3)) >> shift) << 3);
    }
}
