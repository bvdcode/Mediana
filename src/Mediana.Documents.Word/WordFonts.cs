// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Fonts;
using System.Text;

namespace Mediana.Documents.Word
{
    internal partial class WordFonts(WordLayoutOptions options, HashSet<string> warnings, CancellationToken token)
    {
        private readonly Dictionary<WordFontRequest, TrueTypeFont> _requests = [];
        private readonly Dictionary<ReadOnlyMemory<byte>, TrueTypeFont> _programs = [];
        private readonly Dictionary<(TrueTypeFont Font, int Glyph), VectorPath> _outlines = [];
        private readonly Dictionary<(WordFontRequest Request, int Character), TrueTypeFont> _substitutions = [];
        private int _glyphs;
        private int _commands;

        internal WordAtom Character(WordRunFormat format, Rune character)
        {
            token.ThrowIfCancellationRequested();
            if (++_glyphs > options.MaximumGlyphs) { throw new InvalidDataException("Word layout exceeds MaximumGlyphs."); }
            TrueTypeFont font = Font(format);
            double size = format.Baseline == 0 ? format.Size : format.Size * .65;
            if (format.SmallCaps && Rune.IsLower(character)) { character = Rune.ToUpperInvariant(character); size *= .8; }
            int glyph = font.Glyph(character.Value);
            if (glyph == 0 && !Rune.IsWhiteSpace(character))
            {
                WordFontRequest request = new(format.Family, format.Bold, format.Italic);
                ReadOnlyMemory<byte> alternative = ReadOnlyMemory<byte>.Empty;
                if (options.SubstituteMissingFonts)
                {
                    if (_substitutions.TryGetValue((request, character.Value), out TrueTypeFont? prior)) { alternative = prior.Program; }
                    else { alternative = options.FallbackFontResolver?.Invoke(request, character.Value) ?? ReadOnlyMemory<byte>.Empty; }
                }
                if (!alternative.IsEmpty)
                {
                    if (!_programs.TryGetValue(alternative, out TrueTypeFont? resolved))
                    {
                        if (_programs.Count >= options.MaximumFontFaces) { throw new InvalidDataException("Word layout exceeds MaximumFontFaces."); }
                        resolved = new(alternative);
                        _programs.Add(alternative, resolved);
                    }
                    font = resolved;
                    glyph = font.Glyph(character.Value);
                    if (_substitutions.TryAdd((request, character.Value), font))
                    {
                        warnings.Add($"Word glyph U+{character.Value:X4} uses a supplemental font: {font.ReadFaceInfo()?.Family ?? "unnamed"}.");
                    }
                }
                if (glyph == 0)
                {
                    if (!options.SubstituteMissingFonts) { throw new NotSupportedException($"The selected font has no glyph for U+{character.Value:X4}. Supply a font containing this character."); }
                    warnings.Add($"No selected font contains U+{character.Value:X4}; the font's missing-glyph symbol is shown. Supply a suitable font to render this character.");
                }
            }
            if (!_outlines.TryGetValue((font, glyph), out VectorPath? path))
            {
                path = font.Outline(glyph);
                if (path.CommandCount > options.MaximumPathCommands - _commands) { throw new InvalidDataException("Word layout exceeds MaximumPathCommands."); }
                _commands += path.CommandCount;
                _outlines.Add((font, glyph), path);
            }
            double scale = size / font.Units;
            bool space = character.Value is ' ' or '\t';
            return new(format, path, scale, font.Advance(glyph) * scale,
                Math.Max(0, font.Ascent * scale + format.Baseline * format.Size),
                Math.Max(0, -font.Descent * scale - format.Baseline * format.Size), space, space || character.Value == '-', Codepoint: character.Value);
        }

        internal WordAtom Mark(WordRunFormat format) => Character(format, new Rune(' ')) with { Advance = 0, Space = false, BreakAfter = false };

        private TrueTypeFont Font(WordRunFormat format)
        {
            WordFontRequest request = new(format.Family, format.Bold, format.Italic);
            if (_requests.TryGetValue(request, out TrueTypeFont? cached)) { return cached; }
            if (_requests.Count >= options.MaximumFontFaces) { throw new InvalidDataException("Word layout exceeds MaximumFontFaces."); }
            ReadOnlyMemory<byte> program = options.FontResolver?.Invoke(request) ?? ReadOnlyMemory<byte>.Empty;
            if (program.IsEmpty)
            {
                string family = format.Family.ToLowerInvariant() switch
                {
                    "aileron" => "Aileron",
                    "canada1500" => "Canada1500",
                    _ when options.SubstituteMissingFonts => "Canada1500",
                    _ => throw new NotSupportedException("Word font is unavailable: " + format.Family + ". Supply FontResolver or enable SubstituteMissingFonts.")
                };
                program = BundledFontPrograms.GetFont(family, format.Bold, format.Italic);
            }
            if (!_programs.TryGetValue(program, out TrueTypeFont? font))
            {
                if (_programs.Count >= options.MaximumFontFaces) { throw new InvalidDataException("Word layout exceeds MaximumFontFaces."); }
                font = new(program);
                _programs.Add(program, font);
            }
            FontFaceInfo? face = font.ReadFaceInfo();
            if (face is not null && !face.Family.Replace(" ", "", StringComparison.Ordinal).Equals(format.Family.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Word font '" + format.Family + "' is rendered with '" + face.Family + "'. Pagination can change with substituted fonts.");
            }
            _requests.Add(request, font);
            return font;
        }
    }
}
