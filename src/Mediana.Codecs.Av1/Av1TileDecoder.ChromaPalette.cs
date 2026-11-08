// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private int ChromaPalette(int row, int column, int context, int lumaPalette, scoped Span<T> colors)
        {
            int count = Palette(row, column, context, colors, 1, lumaPalette);
            if (count == 0) { return 0; }
            Span<T> values = colors.Slice(count, count);
            bool delta = symbols.ReadBoolean();
            if (delta)
            {
                int bits = SampleBitDepth - 4 + (int)symbols.ReadLiteral(2);
                values[0] = T.CreateTruncating(symbols.ReadLiteral(SampleBitDepth));
                for (int index = 1; index < count; index++)
                {
                    int difference = (int)symbols.ReadLiteral(bits);
                    if (difference != 0 && symbols.ReadBoolean()) { difference = -difference; }
                    values[index] = T.CreateTruncating((int.CreateTruncating(values[index - 1]) + difference) & ((1 << SampleBitDepth) - 1));
                }
            }
            else
            {
                for (int index = 0; index < count; index++) { values[index] = T.CreateTruncating(symbols.ReadLiteral(SampleBitDepth)); }
            }
            return count;
        }
    }
}
