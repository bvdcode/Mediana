// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Svg
{
    internal partial class SvgTrueType
    {
        internal int EncodedGlyph(int code, int platform, int encoding)
        {
            int table = Table("cmap", 4), count = U16(table + 2);
            for (int i = 0; i < count; i++)
            {
                int record = table + 4 + i * 8;
                if (U16(record) != platform || U16(record + 2) != encoding) { continue; }
                int offset = checked(table + Offset(record + 4));
                if (CmapScore(U16(offset)) < 0) { throw new NotSupportedException("Unsupported encoded TrueType cmap format."); }
                int glyph = Cmap(offset, code);
                return glyph >= 0 && glyph < _glyphs ? glyph : 0;
            }
            return -1;
        }
    }
}
