// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using Mediana.Documents.Spreadsheets;
using System.Xml;
using System.Xml.Linq;

namespace Mediana.Documents.Xlsx
{
    public partial class XlsxWorkbook
    {
        private IReadOnlyList<string>? _strings;
        private bool[]? _dateStyles;
        private string? Related(string type)
        {
            OpenXmlRelationship[] matches = _relationships.Where(item => OpenXmlNamespaces.IsOfficeRelationship(item.Type, type)).ToArray();
            if (matches.Length > 1) { throw new InvalidDataException("Duplicate XLSX resource relationship: " + type); }
            return matches.Length == 0 ? null : OpenXmlPackage.Resolve(_main, matches[0]);
        }
        private string SharedString(int index, CancellationToken token)
        {
            if (_strings is null)
            {
                string part = Related("sharedStrings") ?? throw new InvalidDataException("XLSX shared-string table is missing.");
                List<string> strings = [];
                long characters = 0;
                using XmlReader reader = RequireOpen().OpenXmlReader(part, token);
                reader.MoveToContent();
                if (reader.LocalName != "sst" || reader.NamespaceURI != _ns.NamespaceName) { throw new InvalidDataException("Invalid XLSX shared-string table."); }
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si" || reader.NamespaceURI != _ns.NamespaceName) { continue; }
                    if (strings.Count >= _options.MaximumSharedStrings) { throw new InvalidDataException("XLSX exceeds MaximumSharedStrings."); }
                    XElement element;
                    using (XmlReader subtree = reader.ReadSubtree()) { element = XElement.Load(subtree, LoadOptions.PreserveWhitespace); }
                    string text = XlsxText.Read(element, _ns, _options.MaximumStringCharacters - characters);
                    characters += text.Length;
                    strings.Add(text);
                }
                _strings = strings.AsReadOnly();
            }
            return index < _strings.Count ? _strings[index] : throw new InvalidDataException("XLSX shared-string index is outside its table.");
        }
        private bool DateStyle(int index, CancellationToken token)
        {
            if (_dateStyles is null)
            {
                string? part = Related("styles");
                if (part is null) { _dateStyles = [false]; }
                else
                {
                    XElement root = RequireOpen().ReadXml(part, token).Root ?? throw new InvalidDataException("Empty XLSX style table.");
                    if (root.Name != _ns + "styleSheet") { throw new InvalidDataException("Invalid XLSX style table."); }
                    Dictionary<int, string> formats = [];
                    foreach (XElement item in root.Element(_ns + "numFmts")?.Elements(_ns + "numFmt") ?? [])
                    {
                        if (formats.Count >= _options.MaximumStyles || !formats.TryAdd(Integer(Required(item, "numFmtId"), 0, 65535), Required(item, "formatCode")))
                        { throw new InvalidDataException("Duplicate or excessive XLSX number formats."); }
                    }
                    List<bool> styles = [];
                    foreach (XElement item in root.Element(_ns + "cellXfs")?.Elements(_ns + "xf") ?? [])
                    {
                        if (styles.Count >= _options.MaximumStyles) { throw new InvalidDataException("XLSX exceeds MaximumStyles."); }
                        int id = Integer((string?)item.Attribute("numFmtId") ?? "0", 0, 65535);
                        styles.Add(SpreadsheetNumberFormats.IsDateTime(id, formats.GetValueOrDefault(id)));
                    }
                    _dateStyles = styles.Count == 0 ? [false] : styles.ToArray();
                }
            }
            return index < _dateStyles.Length ? _dateStyles[index] : throw new InvalidDataException("XLSX style index is outside its table.");
        }
    }
}
