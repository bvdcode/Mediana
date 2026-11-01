// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal ref struct JpegEntropyReader(ReadOnlySpan<byte> bytes, int position)
    {
        private readonly ReadOnlySpan<byte> bytes = bytes;
        private int current;
        private int remaining;
        internal int Position { get; private set; } = position;

        internal int ReadBits(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                if (remaining == 0)
                {
                    if (Position >= bytes.Length)
                    {
                        throw new InvalidDataException("JPEG entropy data is truncated.");
                    }
                    current = bytes[Position++];
                    if (current == 255 && (Position >= bytes.Length || bytes[Position++] != 0))
                    {
                        throw new InvalidDataException("JPEG entropy data ends before all scan blocks are decoded.");
                    }
                    remaining = 8;
                }
                value = (value << 1) | ((current >> --remaining) & 1);
            }
            return value;
        }

        internal int Receive(int count)
        {
            if (count == 0)
            {
                return 0;
            }
            int value = ReadBits(count);
            if (value < (1 << (count - 1)))
            {
                value -= (1 << count) - 1;
            }
            return value;
        }

        internal void Align()
        {
            int mask = (1 << remaining) - 1;
            if ((current & mask) != mask)
            {
                throw new InvalidDataException("JPEG entropy padding must contain only one bits.");
            }
            remaining = 0;
        }
    }
}
