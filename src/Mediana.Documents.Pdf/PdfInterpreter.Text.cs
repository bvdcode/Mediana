// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using Mediana.Drawing;
using Mediana.Images.Svg;
namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private readonly Dictionary<PdfValue, PdfFont?> _fonts = [];
        private PdfFont? ReadFont(PdfValue dictionary)
        {
            if (_fonts.TryGetValue(dictionary, out PdfFont? cached)) { return cached; }
            PdfFont? font = CreateFont(dictionary);
            _fonts.Add(dictionary, font); return font;
        }
        private PdfFont? CreateFont(PdfValue dictionary)
        {
            string type = Get(dictionary, "Subtype").Name();
            bool composite = type == "Type0";
            if (composite)
            {
                if (Get(dictionary, "Encoding").Text != "Identity-H") { return null; }
                PdfValue descendants = Get(dictionary, "DescendantFonts");
                if (descendants.Items.Length != 1) { throw new InvalidDataException("Invalid PDF descendant font."); }
                dictionary = Resolve(descendants.Items[0]); type = Get(dictionary, "Subtype").Name();
            }
            if (type is not ("TrueType" or "CIDFontType2")) { return null; }
            PdfValue descriptor = Get(dictionary, "FontDescriptor");
            PdfValue file = descriptor.Get("FontFile2");
            if (file.Kind == PdfValueKind.Null) { return null; }
            SvgTrueType face = new(storage.Decode(storage.Object(file, token), token));
            Dictionary<int, int> glyphs = [];
            Dictionary<int, double> widths = [];
            double defaultWidth;
            if (composite)
            {
                defaultWidth = dictionary.Get("DW").Kind == PdfValueKind.Null ? 1000 : Get(dictionary, "DW").Numeric();
                PdfValue map = Get(dictionary, "CIDToGIDMap");
                if (map.Kind == PdfValueKind.Name && map.Text != "Identity") { throw new NotSupportedException("Unknown PDF CIDToGIDMap."); }
                byte[] mapping = dictionary.Get("CIDToGIDMap").Kind == PdfValueKind.Reference ? storage.Decode(storage.Object(dictionary.Get("CIDToGIDMap"), token), token) : [];
                if (mapping.Length % 2 != 0 || mapping.Length > 131072) { throw new InvalidDataException("Invalid PDF glyph map."); }
                for (int code = 0; code < 65536; code++) { glyphs[code] = mapping.Length == 0 ? code : code < mapping.Length / 2 ? BinaryPrimitives.ReadUInt16BigEndian(mapping.AsSpan(code * 2)) : 0; }
                PdfValue[] w = Get(dictionary, "W").Items;
                for (int i = 0; i < w.Length;)
                {
                    int first = w[i++].Integer();
                    if ((uint)first > 65535 || i >= w.Length) { throw new InvalidDataException("Invalid PDF CID widths."); }
                    PdfValue next = Resolve(w[i++]);
                    if (next.Kind == PdfValueKind.Array)
                    { for (int j = 0; j < next.Items.Length; j++) { if (first + j > 65535) { throw new InvalidDataException("CID width overflow."); } widths[first + j] = next.Items[j].Numeric(); } }
                    else
                    {
                        int last = next.Integer();
                        if (last < first || last > 65535 || i >= w.Length) { throw new InvalidDataException("Invalid PDF CID width range."); }
                        double width = w[i++].Numeric();
                        for (int code = first; code <= last; code++) { widths[code] = width; }
                    }
                }
            }
            else
            {
                defaultWidth = descriptor.Get("MissingWidth").Kind == PdfValueKind.Null ? 0 : Get(descriptor, "MissingWidth").Numeric();
                int first = Get(dictionary, "FirstChar").Integer();
                PdfValue[] w = Get(dictionary, "Widths").Items;
                if (first < 0 || first + w.Length > 256) { throw new InvalidDataException("Invalid PDF font widths."); }
                for (int i = 0; i < w.Length; i++) { widths[first + i] = w[i].Numeric(); }
                PdfValue encoding = Get(dictionary, "Encoding");
                string baseName = encoding.Kind == PdfValueKind.Name ? encoding.Text : Get(encoding, "BaseEncoding").Text;
                if (baseName is not ("" or "WinAnsiEncoding")) { return null; }
                bool symbolic = encoding.Kind == PdfValueKind.Null || (Get(descriptor, "Flags").Integer() & 4) != 0;
                for (int code = 0; code < 256; code++)
                {
                    int glyph = symbolic ? face.EncodedGlyph(code, 1, 0) : face.Glyph(WinAnsi(code));
                    if (glyph < 0) { return null; }
                    glyphs[code] = glyph;
                }
                int current = -1;
                foreach (PdfValue difference in Get(encoding, "Differences").Items)
                {
                    if (difference.Kind == PdfValueKind.Number) { current = difference.Integer(); continue; }
                    if ((uint)current > 255) { throw new InvalidDataException("Invalid PDF encoding Differences."); }
                    string name = difference.Name();
                    if (name == ".notdef") { glyphs[current++] = 0; continue; }
                    int unicode = GlyphCode(name);
                    if (unicode < 0) { glyphs[current++] = -1; }
                    else { glyphs[current++] = face.Glyph(unicode); }
                }
            }
            return new(face, glyphs, widths, defaultWidth, composite);
        }
        private void RequireText() { if (!_inText) { throw new InvalidDataException("PDF text operator outside BT/ET."); } }
        private void MoveText(float x, float y) { RequireText(); _text = _textLine = Matrix3x2.CreateTranslation(x, y) * _textLine; }
        private void Advance(float x) { _text = Matrix3x2.CreateTranslation(x, 0) * _text; }
        private void Show(PdfValue text)
        {
            RequireText();
            if (text.Kind != PdfValueKind.String) { throw new InvalidDataException("Expected a PDF text string."); }
            PdfFont? font = _state.Font;
            if (font is null) { _unsupported.Add("Text without a supported embedded font"); return; }
            if (font.Composite && text.Bytes.Length % 2 != 0) { throw new InvalidDataException("Odd Identity-H string length."); }
            for (int i = 0; i < text.Bytes.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                int code = font.Composite ? BinaryPrimitives.ReadUInt16BigEndian(text.Bytes.AsSpan(i++, 2)) : text.Bytes[i];
                int glyph = font.Glyphs.GetValueOrDefault(code);
                if (glyph < 0) { _unsupported.Add("Unmapped PDF glyph encoding"); glyph = 0; }
                Matrix3x2 scale = Matrix3x2.CreateScale(_state.FontSize * _state.HorizontalScale / font.Face.Units, _state.FontSize / font.Face.Units);
                Matrix3x2 placement = scale * Matrix3x2.CreateTranslation(0, _state.Rise) * _text * _state.Transform;
                VectorPath source = font.Face.Outline(glyph);
                _operations = checked(_operations + source.CommandCount + 1);
                if (_operations > storage.Budget.Options.MaximumOperations) { throw new InvalidDataException("PDF glyph geometry exceeds the page operation limit."); }
                VectorPath outline = source.Transform(placement);
                if (_state.TextMode is 0 or 2 or 4 or 6) { _commands.Add(new FillCommand(outline, _state.Fill with { A = Channel(_state.FillAlpha) }, FillRule.NonZero, _state.Blend)); }
                if (_state.TextMode is 1 or 2 or 5 or 6) { _commands.Add(new StrokeCommand(outline, _state.Stroke with { A = Channel(_state.StrokeAlpha) }, _state.Pen, _state.Transform, _state.Blend)); }
                if (_state.TextMode >= 4) { _textClip.Append(outline); _hasTextClip = true; }
                double width = font.Widths.GetValueOrDefault(code, font.DefaultWidth);
                float word = !font.Composite && code == 32 ? _state.WordSpace : 0;
                Advance(Float((width * _state.FontSize / 1000 + _state.CharacterSpace + word) * _state.HorizontalScale));
            }
        }
        private static int GlyphCode(string name)
        {
            if (name.Length == 1) { return name[0]; }
            if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length == 7 && int.TryParse(name.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) { return code; }
            return name switch
            {
                "space" => 32,
                "exclam" => 33,
                "quotedbl" => 34,
                "numbersign" => 35,
                "dollar" => 36,
                "percent" => 37,
                "ampersand" => 38,
                "quotesingle" => 39,
                "parenleft" => 40,
                "parenright" => 41,
                "asterisk" => 42,
                "plus" => 43,
                "comma" => 44,
                "hyphen" => 45,
                "period" => 46,
                "slash" => 47,
                "zero" => 48,
                "one" => 49,
                "two" => 50,
                "three" => 51,
                "four" => 52,
                "five" => 53,
                "six" => 54,
                "seven" => 55,
                "eight" => 56,
                "nine" => 57,
                "colon" => 58,
                "semicolon" => 59,
                "less" => 60,
                "equal" => 61,
                "greater" => 62,
                "question" => 63,
                "at" => 64,
                "bracketleft" => 91,
                "backslash" => 92,
                "bracketright" => 93,
                "asciicircum" => 94,
                "underscore" => 95,
                "grave" => 96,
                "braceleft" => 123,
                "bar" => 124,
                "braceright" => 125,
                "asciitilde" => 126,
                _ => -1
            };
        }
        private static int WinAnsi(int code)
        {
            const string specials = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
            return code is >= 128 and < 160 ? specials[code - 128] : code;
        }
    }
}
