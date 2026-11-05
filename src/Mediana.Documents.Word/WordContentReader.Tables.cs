// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal partial class WordContentReader
    {
        private WordTable Table(XElement table, string part)
        {
            XElement? properties = table.Element(W + "tblPr");
            double[] columns = table.Element(W + "tblGrid")?.Elements(W + "gridCol")
                .Select(item => WordFormatReader.Positive(context.Length(item, "w"))).ToArray()
                ?? throw new NotSupportedException("Word tables require an explicit column grid.");
            if (columns.Length == 0) { throw new InvalidDataException("Empty Word table grid."); }
            XElement? defaultMargins = properties?.Element(W + "tblCellMar");
            XElement? tableBorders = properties?.Element(W + "tblBorders");
            List<WordTableRow> rows = [];
            XElement[] sourceRows = table.Elements(W + "tr").ToArray();
            for (int rowIndex = 0; rowIndex < sourceRows.Length; rowIndex++)
            {
                XElement row = sourceRows[rowIndex];
                XElement? rowProperties = row.Element(W + "trPr"), height = rowProperties?.Element(W + "trHeight");
                List<WordTableCell> cells = [];
                int column = 0;
                foreach (XElement cell in row.Elements(W + "tc"))
                {
                    XElement? cellProperties = cell.Element(W + "tcPr");
                    if (cellProperties?.Element(W + "vMerge") is not null) { throw new NotSupportedException("Vertically merged Word cells are not supported."); }
                    int span = WordReadContext.Integer(context.Value(cellProperties?.Element(W + "gridSpan")) ?? "1");
                    if (span < 1 || span > columns.Length - column) { throw new InvalidDataException("Invalid Word cell grid span."); }
                    XElement margins = new(W + "tcMar");
                    WordStyles.Merge(margins, defaultMargins);
                    WordStyles.Merge(margins, cellProperties?.Element(W + "tcMar"));
                    XElement borders = new(W + "tcBorders");
                    CopyBorder("top", rowIndex == 0 ? "top" : "insideH");
                    CopyBorder("bottom", rowIndex == sourceRows.Length - 1 ? "bottom" : "insideH");
                    CopyBorder("left", column == 0 ? "left" : "insideV");
                    CopyBorder("right", column + span == columns.Length ? "right" : "insideV");
                    WordStyles.Merge(borders, cellProperties?.Element(W + "tcBorders"));
                    cells.Add(new(Blocks(cell, part), span, Margin("left", 5.4), Margin("right", 5.4), Margin("top", 0), Margin("bottom", 0),
                        _formats.Shading(cellProperties?.Element(W + "shd")), _formats.Borders(borders)));
                    column += span;

                    double Margin(string name, double defaultValue) => context.Length(margins.Element(W + name), "w", defaultValue);
                    void CopyBorder(string target, string source)
                    {
                        if (tableBorders?.Element(W + source) is XElement border) { borders.Add(new XElement(W + target, border.Attributes())); }
                    }
                }
                if (column != columns.Length) { throw new NotSupportedException("Word table rows with omitted grid cells are not supported."); }
                rows.Add(new(cells.AsReadOnly(), context.Length(height, "val"), context.Attribute(height, "hRule") == "exact",
                    WordReadContext.On(rowProperties?.Element(W + "cantSplit"))));
            }
            return new(Array.AsReadOnly(columns), rows.AsReadOnly(), context.Length(properties?.Element(W + "tblInd"), "w"),
                WordFormatReader.Alignment(context.Value(properties?.Element(W + "jc"))));
        }
    }
}
