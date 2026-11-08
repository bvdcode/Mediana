// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public void WritePreparedMacroblock(int x, int y, ReadOnlySpan<byte> samples)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8, scale = plane == 0 ? 1 : 2;
                int offset = plane == 0 ? 0 : 256 + (plane - 1) * 64;
                Span<byte> target = Plane(plane);
                for (int row = 0; row < size; row++)
                { samples.Slice(offset + row * size, size).CopyTo(target.Slice((y / scale + row) * Stride(plane) + x / scale, size)); }
            }
            for (int row = 0; row < 16; row += 4)
                for (int column = 0; column < 16; column += 4)
                { Modes[BlockIndex(0, x + column, y + row)] &= 127; }
        }
    }
}
