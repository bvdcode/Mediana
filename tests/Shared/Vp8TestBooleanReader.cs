// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class Vp8TestBooleanReader
    {
        private readonly ReadOnlyMemory<byte> encoded;
        private int position = 2;
        private uint value;
        private uint range = 255;
        private int bits = 8;

        internal Vp8TestBooleanReader(ReadOnlyMemory<byte> input)
        {
            encoded = input;
            value = (uint)((input.Span[0] << 8) | input.Span[1]);
        }

        internal bool Read(int probability = 128)
        {
            uint split = 1 + ((range - 1) * (uint)probability >> 8);
            bool result = value >= (split << 8);
            if (result) { value -= split << 8; range -= split; }
            else { range = split; }
            while (range < 128)
            {
                range <<= 1;
                value <<= 1;
                if (--bits == 0)
                {
                    if (position >= encoded.Length) { throw new InvalidDataException("Truncated VP8 arithmetic partition."); }
                    value |= encoded.Span[position++];
                    bits = 8;
                }
            }
            return result;
        }

        internal int Literal(int bits)
        {
            int result = 0;
            for (int bit = 0; bit < bits; bit++) { result = (result << 1) | (Read() ? 1 : 0); }
            return result;
        }
    }
}
