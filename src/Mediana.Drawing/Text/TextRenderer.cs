// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing.Fonts;
using System.Numerics;

namespace Mediana.Drawing
{
    /// <summary>Single-consumer font, outline and whole-text-element resolution for one layout operation.
    /// Reuse TextOptions across operations; each renderer owns its mutable caches. Paragraph layout and shaping
    /// remain the responsibility of the document format.</summary>
    public partial class TextRenderer
    {
        private readonly TextOptions _options;
        private readonly Dictionary<FontRequest, TrueTypeFont[]> _requests = [];
        private readonly Dictionary<ReadOnlyMemory<byte>, TrueTypeFont> _programs = [];
        private readonly Dictionary<(TrueTypeFont Font, int Glyph), VectorPath> _outlines = [];
        private readonly Dictionary<(FontRequest Request, int Character), TrueTypeFont?> _supplemental = [];
        private long _fontBytes;
        private int _commands;

        public TextRenderer(TextOptions? options = null)
        {
            _options = options ?? new();
            if (_options.MaximumFontFaces <= 0 || _options.MaximumFontBytes <= 0 || _options.MaximumInlineGraphics <= 0
                || _options.MaximumPathCommands <= 0 || _options.SubstituteFamilies is null)
            { throw new ArgumentOutOfRangeException(nameof(options)); }
        }

        /// <summary>Returns the first available face, or null when neither a resolver nor an enabled substitution supplies one.</summary>
        public TrueTypeFont? ResolveFont(FontRequest request, CancellationToken cancellationToken = default) =>
            Fonts(request, cancellationToken).FirstOrDefault();

        /// <summary>Selects a Unicode glyph. Index zero indicates a missing glyph; no character code is treated as a glyph index.</summary>
        public (TrueTypeFont Font, int Glyph) ResolveGlyph(FontRequest request, int codepoint, CancellationToken cancellationToken = default)
        {
            if (!System.Text.Rune.IsValid(codepoint)) { throw new ArgumentOutOfRangeException(nameof(codepoint)); }
            TrueTypeFont[] fonts = Fonts(request, cancellationToken);
            if (fonts.Length == 0) { throw new NotSupportedException("Font is unavailable: " + request.Family + ". Supply TextOptions.FontResolver or enable font substitution."); }
            foreach (TrueTypeFont font in fonts)
            {
                int glyph = font.Glyph(codepoint);
                if (glyph != 0) { return (font, glyph); }
            }
            if (_options.SubstituteMissingFonts && _options.FallbackFontResolver is not null)
            {
                if (!_supplemental.TryGetValue((request, codepoint), out TrueTypeFont? font))
                {
                    ReadOnlyMemory<byte> program = _options.FallbackFontResolver(request, codepoint);
                    cancellationToken.ThrowIfCancellationRequested();
                    font = program.IsEmpty ? null : LoadFont(program);
                    _supplemental.Add((request, codepoint), font);
                }
                if (font is not null && font.Glyph(codepoint) is int glyph && glyph != 0) { return (font, glyph); }
            }
            return (fonts[0], 0);
        }

        /// <summary>Returns an independently mutable outline in the font's design coordinates.</summary>
        public VectorPath Outline(TrueTypeFont font, int glyph) => BorrowOutline(font, glyph).Transform(Matrix3x2.Identity);

        /// <summary>Draws a glyph with caller-selected placement and color. Font coordinates have an upward Y axis.</summary>
        public void DrawGlyph(RasterCanvas canvas, TrueTypeFont font, int glyph, Rgba32 color, Matrix3x2 transform, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            cancellationToken.ThrowIfCancellationRequested();
            canvas.FillPath(BorrowOutline(font, glyph), color, transform, cancellationToken: cancellationToken);
        }

        internal VectorPath BorrowOutline(TrueTypeFont font, int glyph)
        {
            ArgumentNullException.ThrowIfNull(font);
            if (!_outlines.TryGetValue((font, glyph), out VectorPath? path))
            {
                path = font.BorrowOutline(glyph);
                if (path.CommandCount > _options.MaximumPathCommands - _commands) { throw new InvalidDataException("Text outlines exceed MaximumPathCommands."); }
                _commands += path.CommandCount;
                _outlines.Add((font, glyph), path);
            }
            return path;
        }

        private TrueTypeFont[] Fonts(FontRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Family);
            token.ThrowIfCancellationRequested();
            if (_requests.TryGetValue(request, out TrueTypeFont[]? cached)) { return cached; }
            if (_requests.Count >= _options.MaximumFontFaces) { throw new InvalidDataException("Text requests exceed MaximumFontFaces."); }
            List<TrueTypeFont> fonts = [];
            foreach (string candidate in request.Family.Split(','))
            {
                FontRequest face = request with { Family = candidate.Trim().Trim('\'', '"') };
                ReadOnlyMemory<byte> program = _options.FontResolver?.Invoke(face) ?? default;
                token.ThrowIfCancellationRequested();
                if (program.IsEmpty && BundledFamily(face.Family) is string bundled)
                { program = BundledFontPrograms.GetFont(bundled, face.Bold, face.Italic); }
                if (!program.IsEmpty) { Add(program); }
            }
            if (_options.SubstituteMissingFonts)
            {
                foreach (string family in _options.SubstituteFamilies)
                { Add(BundledFontPrograms.GetFont(family, request.Bold, request.Italic)); }
            }
            TrueTypeFont[] result = fonts.ToArray();
            _requests.Add(request, result);
            return result;

            void Add(ReadOnlyMemory<byte> program)
            {
                TrueTypeFont font = LoadFont(program);
                if (!fonts.Contains(font)) { fonts.Add(font); }
            }
        }

        private TrueTypeFont LoadFont(ReadOnlyMemory<byte> program)
        {
            if (_programs.TryGetValue(program, out TrueTypeFont? cached)) { return cached; }
            if (_programs.Count >= _options.MaximumFontFaces) { throw new InvalidDataException("Text fonts exceed MaximumFontFaces."); }
            if (program.Length > _options.MaximumFontBytes - _fontBytes) { throw new InvalidDataException("Text fonts exceed MaximumFontBytes."); }
            TrueTypeFont font = new(program);
            if (font.Program.Length > _options.MaximumFontBytes - _fontBytes) { throw new InvalidDataException("Decoded text fonts exceed MaximumFontBytes."); }
            _fontBytes += font.Program.Length;
            _programs.Add(program, font);
            return font;
        }

        private static string? BundledFamily(string family) => family.ToUpperInvariant() switch
        {
            "AILERON" => "Aileron",
            "CANADA1500" => "Canada1500",
            _ => null
        };
    }
}
