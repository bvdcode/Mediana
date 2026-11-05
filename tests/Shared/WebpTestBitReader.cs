// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class WebpTestBitReader(ReadOnlyMemory<byte> data)
    {
        private int position;
        internal int Read(int count)
        {
            int value = 0;
            for (int bit = 0; bit < count; bit++)
            {
                if (position >= data.Length * 8L) { throw new InvalidDataException("Truncated WebP reference bitstream."); }
                value |= ((data.Span[position >> 3] >> (position & 7)) & 1) << bit;
                position++;
            }
            return value;
        }
    }
}
