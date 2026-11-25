// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dv
{
    internal ref struct DvBitReader(ReadOnlySpan<byte> data, int bitLength)
    {
        private readonly ReadOnlySpan<byte> bytes = data;
        private int position;

        internal readonly int Remaining => bitLength - position;

        internal readonly int Peek(int count)
        {
            if (count == 0)
            {
                return 0;
            }
            int index = position >> 3;
            int shift = position & 7;
            int value = bytes[index] << 16;
            if (shift + count > 8)
            {
                value |= bytes[index + 1] << 8;
            }
            if (shift + count > 16)
            {
                value |= bytes[index + 2];
            }
            return (value >> (24 - shift - count)) & ((1 << count) - 1);
        }

        internal int Read(int count)
        {
            int value = Peek(count);
            position += count;
            return value;
        }

        internal void Skip(int count) => position += count;
    }
}
