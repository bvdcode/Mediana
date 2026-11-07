// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static class TheoraInverseDct
    {
        internal static void Transform(Span<short> block)
        {
            Span<short> column = stackalloc short[8];
            for (int row = 0; row < 8; row++) { Transform1D(block.Slice(row * 8, 8)); }
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++) { column[y] = block[y * 8 + x]; }
                Transform1D(column);
                for (int y = 0; y < 8; y++) { block[y * 8 + x] = (short)((column[y] + 8) >> 4); }
            }
        }

        internal static void Transform1D(Span<short> values)
        {
            int a = (46341 * (short)(values[0] + values[4])) >> 16;
            int b = (46341 * (short)(values[0] - values[4])) >> 16;
            int c = ((25080 * values[2]) >> 16) - ((60547 * values[6]) >> 16);
            int d = ((60547 * values[2]) >> 16) + ((25080 * values[6]) >> 16);
            int e = ((12785 * values[1]) >> 16) - ((64277 * values[7]) >> 16);
            int f = ((54491 * values[5]) >> 16) - ((36410 * values[3]) >> 16);
            int g = ((36410 * values[5]) >> 16) + ((54491 * values[3]) >> 16);
            int h = ((64277 * values[1]) >> 16) + ((12785 * values[7]) >> 16);
            int r = e + f; f = (46341 * (short)(e - f)) >> 16; e = r;
            r = h + g; g = (46341 * (short)(h - g)) >> 16; h = r;
            r = a + d; d = a - d; a = r;
            r = b + c; c = b - c; b = r;
            r = g + f; f = g - f; g = r;
            values[0] = (short)(a + h); values[1] = (short)(b + g); values[2] = (short)(c + f); values[3] = (short)(d + e);
            values[4] = (short)(d - e); values[5] = (short)(c - f); values[6] = (short)(b - g); values[7] = (short)(a - h);
        }
    }
}
