// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    public partial class TextRenderer
    {
        private readonly Dictionary<string, InlineGraphic> _graphics = new(StringComparer.Ordinal);

        /// <summary>Resolves a complete grapheme without stripping joiners, presentation selectors or modifiers.</summary>
        public InlineGraphic? ResolveGraphic(ReadOnlySpan<char> element, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_options.InlineGraphicProvider is null) { return null; }
            if (_graphics.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(element, out InlineGraphic? cached)) { return cached; }
            InlineGraphic? graphic = _options.InlineGraphicProvider.Resolve(element, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (graphic is null) { return null; }
            if (!double.IsFinite(graphic.Advance) || graphic.Advance <= 0 || !double.IsFinite(graphic.Ascent)
                || graphic.Ascent < 0 || !double.IsFinite(graphic.Descent) || graphic.Descent < 0 || graphic.Ascent + graphic.Descent <= 0)
            { throw new InvalidDataException("Inline graphic metrics must be finite and have positive width and height."); }
            if (_graphics.Count >= _options.MaximumInlineGraphics) { throw new InvalidDataException("Text graphics exceed MaximumInlineGraphics."); }
            _graphics.Add(new(element), graphic);
            return graphic;
        }
    }
}
