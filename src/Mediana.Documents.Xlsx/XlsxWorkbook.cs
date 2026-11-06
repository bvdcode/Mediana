// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using Mediana.Documents.Spreadsheets;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Mediana.Documents.Xlsx
{
    public partial class XlsxWorkbook : SpreadsheetWorkbook
    {
        private OpenXmlPackage? _package;
        private readonly XlsxReaderOptions _options;
        private readonly XNamespace _ns;
        private readonly string[] _parts;
        private readonly IReadOnlyList<OpenXmlRelationship> _relationships;
        private readonly string _main;
        public override IReadOnlyList<SpreadsheetSheet> Sheets { get; }
        public override SpreadsheetDateSystem DateSystem { get; }

        internal XlsxWorkbook(OpenXmlPackage package, XlsxReaderOptions options, CancellationToken token)
        {
            _package = package;
            _options = options;
            _main = package.MainPart(token);
            if (package.ContentType(_main) != XlsxReader.WorkbookContentType) { throw new NotSupportedException("The OPC main part is not an XLSX workbook."); }
            XElement root = package.ReadXml(_main, token).Root ?? throw new InvalidDataException("Empty XLSX workbook.");
            _ns = root.Name.Namespace;
            if (root.Name.LocalName != "workbook" || _ns != OpenXmlNamespaces.Spreadsheet && _ns != OpenXmlNamespaces.StrictSpreadsheet)
            { throw new InvalidDataException("Invalid SpreadsheetML workbook namespace or root."); }
            DateSystem = (string?)root.Element(_ns + "workbookPr")?.Attribute("date1904") switch
            {
                null or "0" or "false" => SpreadsheetDateSystem.Excel1900,
                "1" or "true" => SpreadsheetDateSystem.Excel1904,
                _ => throw new InvalidDataException("Invalid XLSX date1904 value.")
            };
            _relationships = package.ReadRelationships(_main, token);
            Dictionary<string, OpenXmlRelationship> relationships = _relationships.ToDictionary(item => item.Id, StringComparer.Ordinal);
            List<SpreadsheetSheet> sheets = [];
            List<string> parts = [];
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            HashSet<int> ids = [];
            foreach (XElement sheet in root.Element(_ns + "sheets")?.Elements(_ns + "sheet") ?? [])
            {
                token.ThrowIfCancellationRequested();
                if (sheets.Count >= options.MaximumSheets) { throw new InvalidDataException("XLSX exceeds MaximumSheets."); }
                string name = Required(sheet, "name");
                if (!names.Add(name) || !ids.Add(Integer(Required(sheet, "sheetId"), 1, int.MaxValue)))
                { throw new InvalidDataException("Duplicate XLSX sheet name or identifier."); }
                string? id = (string?)sheet.Attribute(XName.Get("id", OpenXmlNamespaces.OfficeRelationships))
                    ?? (string?)sheet.Attribute(XName.Get("id", OpenXmlNamespaces.StrictOfficeRelationships));
                if (id is null || !relationships.TryGetValue(id, out OpenXmlRelationship? relationship))
                { throw new InvalidDataException("Missing XLSX sheet relationship."); }
                SpreadsheetSheetKind kind;
                if (OpenXmlNamespaces.IsOfficeRelationship(relationship.Type, "worksheet")) { kind = SpreadsheetSheetKind.Worksheet; }
                else if (OpenXmlNamespaces.IsOfficeRelationship(relationship.Type, "chartsheet")) { kind = SpreadsheetSheetKind.Chart; }
                else if (OpenXmlNamespaces.IsOfficeRelationship(relationship.Type, "dialogsheet")) { kind = SpreadsheetSheetKind.Dialog; }
                else if (OpenXmlNamespaces.IsOfficeRelationship(relationship.Type, "macrosheet")) { kind = SpreadsheetSheetKind.Macro; }
                else { throw new NotSupportedException("Unsupported XLSX sheet relationship type: " + relationship.Type); }
                SpreadsheetSheetVisibility visibility = (string?)sheet.Attribute("state") switch
                {
                    null or "visible" => SpreadsheetSheetVisibility.Visible,
                    "hidden" => SpreadsheetSheetVisibility.Hidden,
                    "veryHidden" => SpreadsheetSheetVisibility.VeryHidden,
                    _ => throw new InvalidDataException("Invalid XLSX sheet visibility.")
                };
                parts.Add(package.CanonicalPart(OpenXmlPackage.Resolve(_main, relationship)));
                sheets.Add(new(sheets.Count, name, visibility, kind));
            }
            _parts = parts.ToArray();
            Sheets = sheets.AsReadOnly();
        }

        public override IEnumerable<SpreadsheetRow> ReadRows(int sheetIndex, CancellationToken cancellationToken = default)
        {
            OpenXmlPackage package = RequireOpen();
            ArgumentOutOfRangeException.ThrowIfNegative(sheetIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sheetIndex, Sheets.Count);
            if (Sheets[sheetIndex].Kind != SpreadsheetSheetKind.Worksheet) { throw new NotSupportedException("The selected XLSX sheet does not contain worksheet rows."); }
            using XmlReader reader = package.OpenXmlReader(_parts[sheetIndex], cancellationToken);
            reader.MoveToContent();
            if (reader.LocalName != "worksheet" || reader.NamespaceURI != _ns.NamespaceName) { throw new InvalidDataException("Invalid XLSX worksheet root."); }
            int previous = -1;
            int dataDepth = -1;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheetData" && reader.NamespaceURI == _ns.NamespaceName)
                { if (!reader.IsEmptyElement) { dataDepth = reader.Depth; } continue; }
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == dataDepth) { yield break; }
                if (dataDepth < 0 || reader.NodeType != XmlNodeType.Element || reader.LocalName != "row" || reader.NamespaceURI != _ns.NamespaceName || reader.Depth != dataDepth + 1) { continue; }
                int index = reader.GetAttribute("r") is string value ? Integer(value, 1, Math.Min(_options.MaximumRows, 1_048_576)) - 1 : previous + 1;
                if (index <= previous || index >= _options.MaximumRows || index >= 1_048_576) { throw new InvalidDataException("XLSX rows must be increasing and within their limit."); }
                previous = index;
                SpreadsheetRow? parsed = ReadRow(reader,index,cancellationToken);
                if (parsed is not null) { yield return parsed; }
            }
        }

        private SpreadsheetRow? ReadRow(XmlReader reader,int index,CancellationToken cancellationToken)
        {
                XElement row;
                using (XmlReader subtree = reader.ReadSubtree()) { row = XElement.Load(subtree, LoadOptions.PreserveWhitespace); }
                List<SpreadsheetCell> cells = [];
                int column = -1;
                long characters = 0;
                foreach (XElement cell in row.Elements(_ns + "c"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int next = cell.Attribute("r") is XAttribute address ? Column(address.Value, index) : column + 1;
                    if (next <= column || next >= _options.MaximumColumns || next >= 16_384) { throw new InvalidDataException("XLSX cells must be increasing and within their column limit."); }
                    column = next;
                    SpreadsheetCell parsed = ReadCell(cell, next, cancellationToken);
                    if (parsed.Value.Kind == SpreadsheetValueKind.Text && (string?)cell.Attribute("t") != "s") { characters += parsed.Value.Text.Length; }
                    if (characters > _options.MaximumStringCharacters) { throw new InvalidDataException("XLSX row exceeds its string limit."); }
                    cells.Add(parsed);
                }
                return cells.Count == 0 ? null : new(index,cells.AsReadOnly());
        }

        public override void Dispose() { _package?.Dispose(); _package = null; }
        private OpenXmlPackage RequireOpen() => _package ?? throw new ObjectDisposedException(nameof(XlsxWorkbook));
        private static string Required(XElement element, string name) => (string?)element.Attribute(name) is { Length: > 0 } value
            ? value : throw new InvalidDataException("Missing XLSX attribute: " + name);
        private static int Integer(string text, int minimum, int maximum) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= minimum && value <= maximum
            ? value : throw new InvalidDataException("Invalid XLSX integer: " + text);
        private static int Column(string reference, int row)
        {
            int column = 0, offset = 0;
            while (offset < reference.Length && reference[offset] is >= 'A' and <= 'Z')
            {
                if (column > 16_384) { throw new InvalidDataException("XLSX column exceeds XFD."); }
                column = column * 26 + reference[offset++] - 'A' + 1;
            }
            if (column == 0 || Integer(reference[offset..], 1, 1_048_576) != row + 1) { throw new InvalidDataException("XLSX cell reference does not match its row."); }
            return column - 1;
        }
    }
}
