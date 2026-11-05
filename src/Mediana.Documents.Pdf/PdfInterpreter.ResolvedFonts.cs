// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing.Fonts;

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private long _fontProgramBytes;

        private PdfFont? CreateResolvedFont(PdfValue source, PdfValue dictionary, bool composite)
        {
            PdfValue unicode = source.Get("ToUnicode");
            if (composite && unicode.Kind == PdfValueKind.Null) { return null; }
            string name = Get(source, "BaseFont").Name();
            ReadOnlyMemory<byte> program = storage.Budget.Options.FontResolver!(new(name, composite));
            token.ThrowIfCancellationRequested();
            if (program.IsEmpty) { return null; }
            if (program.Length > storage.Budget.Options.MaximumDecodedBytes - _fontProgramBytes)
            { throw new InvalidDataException("PDF page font programs exceed the decoded byte limit."); }
            _fontProgramBytes += program.Length;
            TrueTypeFont face = new(program);
            Dictionary<int, int> characters = unicode.Kind == PdfValueKind.Null ? [] : ReadToUnicode(unicode, composite);
            Dictionary<int, int> glyphs = [];
            Dictionary<int, double> widths;
            double defaultWidth;
            if (composite)
            {
                (widths, defaultWidth) = CompositeWidths(dictionary);
                ChargeOperations(characters.Count);
                foreach ((int code, int scalar) in characters) { glyphs[code] = ResolveGlyph(scalar); }
            }
            else
            {
                PdfValue encoding = Get(dictionary, "Encoding");
                string baseName = encoding.Kind == PdfValueKind.Name ? encoding.Name() : Get(encoding, "BaseEncoding").Text;
                if (baseName is not ("" or "StandardEncoding" or "WinAnsiEncoding")) { return null; }
                for (int code = 0; code < 256; code++)
                {
                    int scalar = characters.TryGetValue(code, out int mapped) ? mapped
                        : baseName == "WinAnsiEncoding" ? WinAnsi(code)
                        : code switch { 39 => 0x2019, 96 => 0x2018, >= 32 and <= 126 => code, _ => -1 };
                    glyphs[code] = ResolveGlyph(scalar);
                }
                int current = -1;
                foreach (PdfValue difference in Get(encoding, "Differences").Items)
                {
                    if (difference.Kind == PdfValueKind.Number) { current = difference.Integer(); continue; }
                    if ((uint)current > 255) { throw new InvalidDataException("Invalid PDF encoding Differences."); }
                    string glyphName = difference.Name();
                    if (!characters.ContainsKey(current))
                    { glyphs[current] = glyphName == ".notdef" ? 0 : ResolveGlyph(GlyphCode(glyphName)); }
                    current++;
                }
                defaultWidth = 0;
                if (dictionary.Get("Widths").Kind == PdfValueKind.Null && StandardFont(name))
                {
                    widths = [];
                    foreach ((int code, int glyph) in glyphs)
                    { if (glyph >= 0) { widths[code] = face.Advance(glyph) * 1000 / face.Units; } }
                    _warnings.Add("Unembedded PDF font '" + name + "' uses the supplied font's advance metrics.");
                }
                else
                {
                    widths = SimpleWidths(dictionary);
                    PdfValue missing = Get(Get(dictionary, "FontDescriptor"), "MissingWidth");
                    if (missing.Kind != PdfValueKind.Null) { defaultWidth = missing.Numeric(); }
                }
            }
            _warnings.Add("Unembedded PDF font '" + name + "' uses a caller-supplied font program.");
            return new PdfOutlineFont(face, glyphs, widths, defaultWidth, composite);
            int ResolveGlyph(int scalar)
            {
                if (scalar < 0) { return -1; }
                int glyph = face.Glyph(scalar);
                return glyph == 0 ? -1 : glyph;
            }
        }

        private static bool StandardFont(string name) => name is "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique"
            or "Helvetica-BoldOblique" or "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic"
            or "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique";
    }
}
