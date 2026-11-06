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
        private readonly Dictionary<FontProgram, TrueTypeFont> _programs = [];
        private readonly Dictionary<ReadOnlyMemory<byte>, FontProgram> _sources = [];
        private readonly Dictionary<(TrueTypeFont Font, int Glyph), VectorPath> _outlines = [];
        private readonly Dictionary<(FontRequest Request, int Character), TrueTypeFont?> _supplemental = [];
        private long _fontBytes;
        private int _commands;
        private int _preparedPoints;
        private readonly Dictionary<(TrueTypeFont Font, int Glyph, Matrix3x2 Transform), FlattenedPath> _prepared = [];

        public TextRenderer(TextOptions? options = null)
        {
            _options = options ?? new();
            if (_options.MaximumFontFaces <= 0 || _options.MaximumFontBytes <= 0 || _options.MaximumInlineGraphics <= 0
                || _options.MaximumPathCommands <= 0 || _options.SubstituteFamilies is null || _options.FallbackFamilies is null)
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
            if (_options.SubstituteMissingFonts)
            {
                if (!_supplemental.TryGetValue((request, codepoint), out TrueTypeFont? font))
                {
                    font = ResolveFallback(request, codepoint, cancellationToken);
                    _supplemental.Add((request, codepoint), font);
                }
                if (font is not null && font.Glyph(codepoint) is int glyph && glyph != 0) { return (font, glyph); }
            }
            return (fonts[0], 0);
        }

        private TrueTypeFont? ResolveFallback(FontRequest request, int codepoint, CancellationToken token)
        {
            FontProgram? shared = _options.FallbackFontProgramResolver?.Invoke(request, codepoint);
            ReadOnlyMemory<byte> program = shared is null ? _options.FallbackFontResolver?.Invoke(request, codepoint) ?? default : default;
            token.ThrowIfCancellationRequested();
            TrueTypeFont? custom = shared is not null ? LoadFont(shared) : program.IsEmpty ? null : LoadFont(program);
            if (custom is not null && custom.Glyph(codepoint) != 0) { return custom; }
            foreach (string family in _options.FallbackFamilies)
            {
                token.ThrowIfCancellationRequested();
                FontProgram candidate = BundledFontPrograms.GetProgram(family, request.Bold, request.Italic);
                if (candidate.Glyph(codepoint) != 0) { return LoadFont(candidate); }
            }
            return null;
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

        /// <summary>Draws a glyph with fractional pixel coverage at the canvas resolution.</summary>
        public void DrawGlyphAntialiased(RasterCanvas canvas, TrueTypeFont font, int glyph, Rgba32 color, Matrix3x2 transform, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            cancellationToken.ThrowIfCancellationRequested();
            Vector2 offset = transform.Translation;
            transform.Translation = Vector2.Zero;
            (TrueTypeFont Font, int Glyph, Matrix3x2 Transform) key = (font, glyph, transform);
            if (!_prepared.TryGetValue(key, out FlattenedPath? path))
            {
                path = new(BorrowOutline(font, glyph), transform, cancellationToken);
                if (path.PointCount > _options.MaximumPathCommands - _preparedPoints)
                {
                    _prepared.Clear();
                    _preparedPoints = 0;
                }
                if (path.PointCount <= _options.MaximumPathCommands)
                {
                    _prepared.Add(key, path);
                    _preparedPoints += path.PointCount;
                }
            }
            canvas.FillPathAntialiased(path, color, offset, cancellationToken);
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
                FontProgram? shared = _options.FontProgramResolver?.Invoke(face);
                ReadOnlyMemory<byte> program = shared is null ? _options.FontResolver?.Invoke(face) ?? default : default;
                token.ThrowIfCancellationRequested();
                if (shared is null && program.IsEmpty && BundledFamily(face.Family) is string bundled)
                { shared = BundledFontPrograms.GetProgram(bundled, face.Bold, face.Italic); }
                if (shared is not null) { Add(LoadFont(shared)); }
                else if (!program.IsEmpty) { Add(LoadFont(program)); }
            }
            if (_options.SubstituteMissingFonts)
            {
                foreach (string family in _options.SubstituteFamilies)
                { Add(LoadFont(BundledFontPrograms.GetProgram(family, request.Bold, request.Italic))); }
            }
            TrueTypeFont[] result = fonts.ToArray();
            _requests.Add(request, result);
            return result;

            void Add(TrueTypeFont font)
            {
                if (!fonts.Contains(font)) { fonts.Add(font); }
            }
        }

        private TrueTypeFont LoadFont(ReadOnlyMemory<byte> program)
        {
            if (!_sources.TryGetValue(program, out FontProgram? shared))
            {
                if (program.Length > _options.MaximumFontBytes - _fontBytes) { throw new InvalidDataException("Text fonts exceed MaximumFontBytes."); }
                shared = new(program);
                _sources.Add(program, shared);
            }
            return LoadFont(shared);
        }

        private TrueTypeFont LoadFont(FontProgram program)
        {
            if (_programs.TryGetValue(program, out TrueTypeFont? cached)) { return cached; }
            if (_programs.Count >= _options.MaximumFontFaces) { throw new InvalidDataException("Text fonts exceed MaximumFontFaces."); }
            if (program.Program.Length > _options.MaximumFontBytes - _fontBytes) { throw new InvalidDataException("Decoded text fonts exceed MaximumFontBytes."); }
            TrueTypeFont font = new(program);
            _fontBytes += program.Program.Length;
            _programs.Add(program, font);
            return font;
        }

        private static string? BundledFamily(string family) => family.ToUpperInvariant() switch
        {
            "AILERON" => "Aileron",
            "CANADA1500" => "Canada1500",
            "NOTO SANS SYMBOLS 2" => "Noto Sans Symbols 2",
            _ => null
        };
    }
}
