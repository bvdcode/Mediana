// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3
{
    internal ref struct Ac3BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private int position;
        public readonly int Position => position;
        public readonly int Remaining => data.Length * 8 - position;
        public bool Flag() => Read(1) != 0;
        public int Read(int count)
        {
            if (count is < 0 or > 24 || count > Remaining) { throw new InvalidDataException("The AC-3 syncframe is truncated."); }
            int value = 0;
            while (count > 0)
            {
                int take = Math.Min(count, 8 - (position & 7));
                value = value << take | (data[position >> 3] >> (8 - (position & 7) - take) & ((1 << take) - 1));
                position += take; count -= take;
            }
            return value;
        }
        public void Skip(int count)
        {
            if (count < 0 || count > Remaining) { throw new InvalidDataException("The AC-3 syncframe is truncated."); }
            position += count;
        }
    }
}
