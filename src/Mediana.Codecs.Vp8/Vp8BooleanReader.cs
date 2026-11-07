// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal class Vp8BooleanReader
    {
        private readonly ReadOnlyMemory<byte> encoded;
        private int position = 2, available = 8;
        private uint value, range = 255;
        internal Vp8BooleanReader(ReadOnlyMemory<byte> encoded)
        {
            if (encoded.Length == 0) { throw new InvalidDataException("Truncated VP8 arithmetic partition."); }
            this.encoded = encoded; value = (uint)(encoded.Span[0] << 8);
            if (encoded.Length > 1) { value |= encoded.Span[1]; }
        }
        internal bool Read(int probability = 128)
        {
            uint split = 1 + (((range - 1) * (uint)probability) >> 8);
            bool bit = value >= (split << 8);
            if (bit) { value -= split << 8; range -= split; } else { range = split; }
            while (range < 128)
            {
                range <<= 1; value <<= 1;
                if (--available == 0)
                {
                    if (position >= encoded.Length) { throw new InvalidDataException("Truncated VP8 arithmetic partition."); }
                    value |= encoded.Span[position++]; available = 8;
                }
            }
            return bit;
        }
        internal int Literal(int bits)
        {
            int value = 0;
            for (int index = 0; index < bits; index++) { value = (value << 1) | (Read() ? 1 : 0); }
            return value;
        }
        internal int Signed(int bits) { int value = Literal(bits); return Read() ? -value : value; }
        internal int Tree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int start = 0)
        {
            int node = start;
            do { node = tree[node + (Read(probabilities[node / 2]) ? 1 : 0)]; } while (node > 0);
            return -node;
        }
    }
}
