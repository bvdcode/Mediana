// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Mediana.Documents.Xlsx
{
    internal static class XlsxText
    {
        internal static string Read(XElement? element, XNamespace ns, long maximum)
        {
            StringBuilder text = new();
            if (element is null) { return ""; }
            foreach (XElement run in element.Descendants(ns + "t"))
            {
                if (run.Ancestors(ns + "rPh").Any()) { continue; }
                if (run.Value.Length > maximum - text.Length) { throw new InvalidDataException("XLSX strings exceed their character limit."); }
                text.Append(run.Value);
            }
            return Decode(text.ToString());
        }
        internal static string Decode(string source)
        {
            StringBuilder result = new(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                if (i <= source.Length - 7 && source[i] == '_' && source[i + 1] is 'x' or 'X' && source[i + 6] == '_'
                    && ushort.TryParse(source.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value))
                { result.Append((char)value); i += 6; }
                else { result.Append(source[i]); }
            }
            string text = result.ToString();
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsSurrogate(text[i])) { continue; }
                if (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[++i]))
                { throw new InvalidDataException("Invalid UTF-16 in XLSX text."); }
            }
            return text;
        }
    }
}
