// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal partial class WordFonts
    {
        private readonly Dictionary<string, InlineGraphic> _graphics = new(StringComparer.Ordinal);

        internal WordAtom? Graphic(WordRunFormat format, ReadOnlySpan<char> element)
        {
            if (options.InlineGraphicProvider is null) { return null; }
            if (!_graphics.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(element, out InlineGraphic? graphic))
            {
                graphic = options.InlineGraphicProvider.Resolve(element, token);
                if (graphic is null) { return null; }
                if (!double.IsFinite(graphic.Advance) || graphic.Advance <= 0 || !double.IsFinite(graphic.Ascent)
                    || graphic.Ascent < 0 || !double.IsFinite(graphic.Descent) || graphic.Descent < 0 || graphic.Ascent + graphic.Descent <= 0)
                {
                    throw new InvalidDataException("Inline graphic metrics must be finite and have positive width and height.");
                }
                if (_graphics.Count >= options.MaximumInlineGraphics) { throw new InvalidDataException("Word layout exceeds MaximumInlineGraphics."); }
                _graphics.Add(new(element), graphic);
            }
            if (++_glyphs > options.MaximumGlyphs) { throw new InvalidDataException("Word layout exceeds MaximumGlyphs."); }
            double size = format.Baseline == 0 ? format.Size : format.Size * .65;
            return new(format, null, size, graphic.Advance * size,
                Math.Max(0, graphic.Ascent * size + format.Baseline * format.Size),
                Math.Max(0, graphic.Descent * size - format.Baseline * format.Size), BreakAfter: true, Graphic: graphic);
        }
    }
}
