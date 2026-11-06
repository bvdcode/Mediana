// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
namespace Mediana.Documents.Pdf
{
    /// <summary>Character mappings and advance metrics only; no embedded font programs are opened.</summary>
    internal class PdfTextFont
    {
        private readonly Dictionary<int,string> _characters = [];
        private readonly Dictionary<int,double> _widths = [];
        private readonly double _defaultWidth;
        internal bool Composite { get; }
        internal PdfTextFont(PdfStorage storage,PdfValue source,CancellationToken token,Action<int> charge,bool includeMappings = true)
        {
            PdfValue Get(PdfValue value,string name) => storage.Resolve(value.Get(name),token);
            Composite = Get(source,"Subtype").Name() == "Type0";
            PdfValue unicode = source.Get("ToUnicode");
            if (includeMappings && unicode.Kind != PdfValueKind.Null)
            { _characters = PdfUnicodeMap.Read(storage,unicode,Composite,token,charge,_ => { }); }
            PdfValue dictionary = source;
            if (Composite)
            {
                if (Get(source,"Encoding").Text != "Identity-H") { throw new NotSupportedException("Text extraction requires an Identity-H composite encoding."); }
                PdfValue descendants = Get(source,"DescendantFonts");
                if (descendants.Items.Length != 1) { throw new InvalidDataException("Invalid PDF descendant font."); }
                dictionary = storage.Resolve(descendants.Items[0],token);
                if (includeMappings && unicode.Kind == PdfValueKind.Null) { throw new NotSupportedException("Composite PDF text requires a ToUnicode mapping or ActualText."); }
                _defaultWidth = dictionary.Get("DW").Kind == PdfValueKind.Null ? 1000 : Get(dictionary,"DW").Numeric();
                PdfValue[] widths = Get(dictionary,"W").Items;
                for (int i = 0; i < widths.Length;)
                {
                    int first = widths[i++].Integer();
                    if ((uint)first > 65535 || i >= widths.Length) { throw new InvalidDataException("Invalid CID width range."); }
                    PdfValue next = storage.Resolve(widths[i++],token);
                    if (next.Kind == PdfValueKind.Array)
                    {
                        charge(next.Items.Length);
                        for (int n = 0; n < next.Items.Length; n++)
                        { if (first+n > 65535) { throw new InvalidDataException("CID width overflow."); } _widths[first+n] = next.Items[n].Numeric(); }
                    }
                    else
                    {
                        int last = next.Integer();
                        if (last < first || last > 65535 || i >= widths.Length) { throw new InvalidDataException("Invalid CID width range."); }
                        double width = widths[i++].Numeric(); charge(last-first+1);
                        for (int code = first; code <= last; code++) { _widths[code] = width; }
                    }
                }
            }
            else
            {
                PdfValue encoding = Get(source,"Encoding");
                string name = encoding.Kind == PdfValueKind.Name ? encoding.Text : Get(encoding,"BaseEncoding").Text;
                string fontName = Get(source,"BaseFont").Text;
                if (includeMappings && unicode.Kind == PdfValueKind.Null && name == "" && (fontName.Contains("Symbol",StringComparison.Ordinal) || fontName.Contains("ZapfDingbats",StringComparison.Ordinal)))
                { throw new NotSupportedException("Symbolic PDF text requires an explicit Unicode mapping."); }
                HashSet<int> mapped = _characters.Keys.ToHashSet();
                for (int code = 0; includeMappings && code < 256; code++)
                {
                    if (_characters.ContainsKey(code)) { continue; }
                    string? value = name switch
                    {
                        "WinAnsiEncoding" => char.ConvertFromUtf32(PdfCharacterNames.WinAnsi(code)),
                        "MacRomanEncoding" => CodePagesEncodingProvider.Instance.GetEncoding(10000)!.GetString(new[] { (byte)code }),
                        "" or "StandardEncoding" => Standard(code),
                        _ when unicode.Kind != PdfValueKind.Null => null,
                        _ => throw new NotSupportedException("Unsupported PDF text encoding: " + name)
                    };
                    if (value is not null) { _characters.Add(code,value); }
                }
                int current = -1;
                foreach (PdfValue difference in includeMappings ? Get(encoding,"Differences").Items : [])
                {
                    if (difference.Kind == PdfValueKind.Number) { current = difference.Integer(); continue; }
                    if ((uint)current > 255) { throw new InvalidDataException("Invalid PDF Differences index."); }
                    if (!mapped.Contains(current))
                    {
                        int scalar = PdfCharacterNames.GlyphCode(difference.Name());
                        _characters.Remove(current);
                        if (scalar >= 0 && Rune.IsValid(scalar)) { _characters[current] = char.ConvertFromUtf32(scalar); }
                    }
                    current++;
                }
                int first = source.Get("FirstChar").Kind == PdfValueKind.Null ? 0 : Get(source,"FirstChar").Integer();
                PdfValue widths = Get(source,"Widths");
                if (first < 0 || first+widths.Items.Length > 256) { throw new InvalidDataException("Invalid simple font widths."); }
                for (int i = 0; i < widths.Items.Length; i++) { _widths[first+i] = widths.Items[i].Numeric(); }
                PdfValue missing = Get(Get(source,"FontDescriptor"),"MissingWidth");
                _defaultWidth = missing.Kind == PdfValueKind.Null ? 0 : missing.Numeric();
            }
        }
        internal (string Text,double Advance) Decode(byte[] bytes,double size,double characterSpace,double wordSpace,double horizontal,bool includeText = true)
        {
            if (Composite && bytes.Length%2 != 0) { throw new InvalidDataException("Odd Identity-H text string."); }
            StringBuilder text = new(); double advance = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                int code = Composite ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i++,2)) : bytes[i];
                if (includeText)
                {
                    if (!_characters.TryGetValue(code,out string? value)) { throw new NotSupportedException("PDF character has no Unicode mapping: " + code); }
                    text.Append(value);
                }
                advance += (_widths.GetValueOrDefault(code,_defaultWidth)*size/1000+characterSpace+(!Composite && code == 32 ? wordSpace : 0))*horizontal;
            }
            return (text.ToString(),advance);
        }
        private static string? Standard(int code) => code switch
        {
            39 => "’",96 => "‘",>=32 and <=126 => char.ConvertFromUtf32(code),
            161 => "¡",162 => "¢",163 => "£",164 => "⁄",165 => "¥",166 => "ƒ",167 => "§",168 => "¤",169 => "'",170 => "“",171 => "«",172 => "‹",173 => "›",174 => "ﬁ",175 => "ﬂ",
            177 => "–",178 => "†",179 => "‡",180 => "·",182 => "¶",183 => "•",184 => "‚",185 => "„",186 => "”",187 => "»",188 => "…",189 => "‰",191 => "¿",
            193 => "`",194 => "´",195 => "ˆ",196 => "˜",197 => "¯",198 => "˘",199 => "˙",200 => "¨",202 => "˚",203 => "¸",205 => "˝",206 => "˛",207 => "ˇ",208 => "—",
            225 => "Æ",227 => "ª",232 => "Ł",233 => "Ø",234 => "Œ",235 => "º",241 => "æ",245 => "ı",248 => "ł",249 => "ø",250 => "œ",251 => "ß",_ => null
        };
    }
}
