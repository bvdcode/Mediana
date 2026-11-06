// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Compression
{
    internal ref struct FaxBitReader(ReadOnlySpan<byte> data, bool lowBitFirst)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private readonly bool lowBitFirst = lowBitFirst;
        private long position;
        internal int Bit()
        {
            if (position >= (long)data.Length * 8) { throw new InvalidDataException("Truncated CCITT fax stream."); }
            int offset = (int)(position & 7);
            int value = data[(int)(position >> 3)] >> (lowBitFirst ? offset : 7 - offset);
            position++;
            return value & 1;
        }
        internal void Align() => position = (position + 7) & ~7L;
        internal void EndOfLine()
        {
            int zeros = 0;
            while (Bit() == 0)
            {
                if (++zeros > 18) { throw new InvalidDataException("Invalid CCITT end-of-line fill."); }
            }
            if (zeros < 11) { throw new InvalidDataException("Missing CCITT end-of-line marker."); }
        }
    }
}
