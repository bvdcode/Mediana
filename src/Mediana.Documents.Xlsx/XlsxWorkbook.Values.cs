// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Spreadsheets;
using System.Globalization;
using System.Xml.Linq;

namespace Mediana.Documents.Xlsx
{
    public partial class XlsxWorkbook
    {
        private SpreadsheetCell ReadCell(XElement cell, int column, CancellationToken token)
        {
            int style = cell.Attribute("s") is XAttribute s ? Integer(s.Value, 0, _options.MaximumStyles - 1) : 0;
            string type = (string?)cell.Attribute("t") ?? "n";
            if (type is not ("n" or "s" or "str" or "b" or "e" or "d" or "inlineStr"))
            { throw new NotSupportedException("Unknown XLSX cell value type: " + type); }
            XElement? value = cell.Element(_ns + "v");
            XElement? formula = cell.Element(_ns + "f");
            bool cached = value is not null && (value.Value.Length != 0 || type == "str");
            SpreadsheetFormula? source = null;
            if (formula is not null)
            {
                SpreadsheetFormulaKind kind = (string?)formula.Attribute("t") switch
                {
                    null or "normal" => SpreadsheetFormulaKind.Normal,
                    "shared" => SpreadsheetFormulaKind.Shared,
                    "array" => SpreadsheetFormulaKind.Array,
                    "dataTable" => SpreadsheetFormulaKind.DataTable,
                    _ => throw new InvalidDataException("Unknown XLSX formula type.")
                };
                int? shared = formula.Attribute("si") is XAttribute si ? Integer(si.Value, 0, int.MaxValue) : null;
                if (kind == SpreadsheetFormulaKind.Shared && shared is null) { throw new InvalidDataException("A shared XLSX formula requires si."); }
                source = new(formula.Value.Length == 0 ? null : formula.Value, default, cached, shared, (string?)formula.Attribute("ref"), kind);
            }
            SpreadsheetValue result;
            if (type == "inlineStr") { result = SpreadsheetValue.FromText(XlsxText.Read(cell.Element(_ns + "is"), _ns, _options.MaximumStringCharacters)); }
            else if (!cached) { result = default; }
            else
            {
                string text = value!.Value;
                result = type switch
                {
                    "s" => SpreadsheetValue.FromText(SharedString(Integer(text, 0, _options.MaximumSharedStrings - 1), token)),
                    "str" => SpreadsheetValue.FromText(XlsxText.Decode(text)),
                    "b" => SpreadsheetValue.FromBoolean(text switch
                    {
                        "0" or "false" => false,
                        "1" or "true" => true,
                        _ => throw new InvalidDataException("Invalid XLSX Boolean value.")
                    }),
                    "e" => SpreadsheetValue.FromError(text),
                    "d" => SpreadsheetValue.FromDateTime(new(text)),
                    "n" => SpreadsheetNumberFormats.Number(Number(text), DateStyle(style, token), DateSystem),
                    _ => throw new NotSupportedException("Unknown XLSX cell value type: " + type)
                };
            }
            return new(column, result, style, source);
        }

        private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? value : throw new InvalidDataException("Invalid XLSX numeric value.");
    }
}
