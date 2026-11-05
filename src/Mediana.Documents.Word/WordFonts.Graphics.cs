// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal partial class WordFonts
    {
        internal WordAtom? Graphic(WordRunFormat format, ReadOnlySpan<char> element)
        {
            InlineGraphic? graphic = _text.ResolveGraphic(element, token);
            if (graphic is null) { return null; }
            if (++_glyphs > options.MaximumGlyphs) { throw new InvalidDataException("Word layout exceeds MaximumGlyphs."); }
            double size = format.Baseline == 0 ? format.Size : format.Size * .65;
            return new(format, null, size, graphic.Advance * size,
                Math.Max(0, graphic.Ascent * size + format.Baseline * format.Size),
                Math.Max(0, graphic.Descent * size - format.Baseline * format.Size), BreakAfter: true, Graphic: graphic);
        }
    }
}
