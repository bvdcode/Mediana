// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Gif
{
    internal ref struct GifLzwReader(ReadOnlySpan<byte> blocks)
    {
        private readonly ReadOnlySpan<byte> blocks = blocks;
        private int position, remaining, count;
        private uint bits;
        internal int Read(int width)
        {
            while (count < width)
            {
                if (remaining == 0)
                {
                    if (position >= blocks.Length || (remaining = blocks[position++]) == 0)
                    { throw new InvalidDataException("GIF LZW stream is missing its end code."); }
                }
                if (position >= blocks.Length) { throw new InvalidDataException("Truncated GIF LZW sub-block."); }
                bits |= (uint)blocks[position++] << count; count += 8; remaining--;
            }
            int value = (int)(bits & ((1u << width) - 1)); bits >>= width; count -= width; return value;
        }
    }
}
