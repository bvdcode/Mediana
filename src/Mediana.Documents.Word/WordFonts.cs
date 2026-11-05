// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Fonts;
using System.Text;

namespace Mediana.Documents.Word
{
    internal partial class WordFonts(WordLayoutOptions options, HashSet<string> warnings, CancellationToken token)
    {
        private readonly TextRenderer _text = new(options.Text);
        private readonly HashSet<(TrueTypeFont Font, int Glyph)> _outlines = [];
        private readonly HashSet<(FontRequest Request, TrueTypeFont Font)> _faces = [];
        private int _glyphs;
        private int _commands;

        internal WordAtom Character(WordRunFormat format, Rune character)
        {
            token.ThrowIfCancellationRequested();
            if (++_glyphs > options.MaximumGlyphs) { throw new InvalidDataException("Word layout exceeds MaximumGlyphs."); }
            double size = format.Baseline == 0 ? format.Size : format.Size * .65;
            if (format.SmallCaps && Rune.IsLower(character)) { character = Rune.ToUpperInvariant(character); size *= .8; }
            FontRequest request = new(format.Family, format.Bold, format.Italic);
            (TrueTypeFont font, int glyph) = _text.ResolveGlyph(request, character.Value, token);
            if (_faces.Add((request, font)))
            {
                FontFaceInfo? face = font.ReadFaceInfo();
                if (face is not null && !face.Family.Replace(" ", "", StringComparison.Ordinal).Equals(format.Family.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
                { warnings.Add("Word font '" + format.Family + "' is rendered with '" + face.Family + "'. Pagination can change with substituted fonts."); }
            }
            if (glyph == 0 && !Rune.IsWhiteSpace(character))
            {
                if (!options.Text.SubstituteMissingFonts) { throw new NotSupportedException($"The selected font has no glyph for U+{character.Value:X4}. Supply a font containing this character."); }
                warnings.Add($"No selected font contains U+{character.Value:X4}; the font's missing-glyph symbol is shown. Supply a suitable font to render this character.");
            }
            VectorPath path = _text.BorrowOutline(font, glyph);
            if (_outlines.Add((font, glyph)))
            {
                if (path.CommandCount > options.MaximumPathCommands - _commands) { throw new InvalidDataException("Word layout exceeds MaximumPathCommands."); }
                _commands += path.CommandCount;
            }
            double scale = size / font.Units;
            bool space = character.Value is ' ' or '\t';
            return new(format, path, scale, font.Advance(glyph) * scale,
                Math.Max(0, font.Ascent * scale + format.Baseline * format.Size),
                Math.Max(0, -font.Descent * scale - format.Baseline * format.Size), space, space || character.Value == '-', Codepoint: character.Value);
        }

        internal WordAtom Mark(WordRunFormat format) => Character(format, new Rune(' ')) with { Advance = 0, Space = false, BreakAfter = false };
    }
}
