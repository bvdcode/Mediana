// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref struct Av1TileGroupReader
    {
        private readonly ReadOnlySpan<byte> data;
        private readonly int end;
        private readonly int sizeBytes;
        private int tile;
        private int offset;

        internal Av1TileGroupReader(ReadOnlySpan<byte> encoded, Av1TileInfo info, int expectedTile, bool frameObu)
        {
            Av1BitReader bits = new(encoded);
            bool hasRange = info.Count > 1 && bits.Flag();
            if (hasRange && frameObu)
            {
                throw new InvalidDataException("An AV1 frame OBU must contain its entire tile grid.");
            }
            int width = info.ColumnLog2 + info.RowLog2;
            tile = hasRange ? bits.Read(width) : 0;
            end = hasRange ? bits.Read(width) : info.Count - 1;
            if (tile != expectedTile || end < tile || end >= info.Count)
            {
                throw new InvalidDataException("The AV1 tile-group range is invalid or out of order.");
            }
            bits.Align();
            data = encoded;
            offset = bits.ByteOffset;
            sizeBytes = info.SizeBytes;
        }

        internal readonly int NextTile => tile;

        internal bool TryRead(out int index, out ReadOnlySpan<byte> encoded)
        {
            if (tile > end)
            {
                index = 0;
                encoded = default;
                return false;
            }
            ulong length = (ulong)(data.Length - offset);
            if (tile != end)
            {
                if (sizeBytes is < 1 or > 4 || sizeBytes > data.Length - offset)
                {
                    throw new InvalidDataException("The AV1 tile-size field is truncated.");
                }
                length = 0;
                for (int shift = 0; shift < sizeBytes; shift++) { length |= (ulong)data[offset++] << (shift * 8); }
                length++;
            }
            if (length == 0 || length > (ulong)(data.Length - offset))
            {
                throw new InvalidDataException("The AV1 tile payload is truncated or empty.");
            }
            index = tile++;
            encoded = data.Slice(offset, (int)length);
            offset += (int)length;
            return true;
        }
    }
}
