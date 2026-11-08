// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private int ChromaPalette(int row, int column, int context, int lumaPalette, scoped Span<byte> colors)
        {
            int count = Palette(row, column, context, colors, 1, lumaPalette);
            if (count == 0) { return 0; }
            Span<byte> values = colors.Slice(count, count);
            bool delta = symbols.ReadBoolean();
            if (delta)
            {
                int bits = 4 + (int)symbols.ReadLiteral(2);
                values[0] = (byte)symbols.ReadLiteral(8);
                for (int index = 1; index < count; index++)
                {
                    int difference = (int)symbols.ReadLiteral(bits);
                    if (difference != 0 && symbols.ReadBoolean()) { difference = -difference; }
                    values[index] = (byte)((values[index - 1] + difference) & 255);
                }
            }
            else
            {
                for (int index = 0; index < count; index++) { values[index] = (byte)symbols.ReadLiteral(8); }
            }
            return count;
        }
    }
}
