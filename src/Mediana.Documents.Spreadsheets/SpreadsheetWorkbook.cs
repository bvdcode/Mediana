// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Spreadsheets
{
    /// <summary>Single-consumer workbook borrowing its input until disposal. Row enumerators must be disposed before the workbook.</summary>
    public abstract class SpreadsheetWorkbook : IDisposable, IDocumentTextSource
    {
        public abstract IReadOnlyList<SpreadsheetSheet> Sheets { get; }
        public abstract SpreadsheetDateSystem DateSystem { get; }
        public abstract IEnumerable<SpreadsheetRow> ReadRows(int sheetIndex, CancellationToken cancellationToken = default);
        public abstract void Dispose();

        public IEnumerable<DocumentTextBlock> ReadText(DocumentTextOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            DocumentTextBudget budget = new(options);
            foreach (SpreadsheetSheet sheet in Sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!options.IncludesPart(sheet.Index) || sheet.Kind != SpreadsheetSheetKind.Worksheet) { continue; }
                foreach (SpreadsheetRow row in ReadRows(sheet.Index, cancellationToken))
                {
                    foreach (SpreadsheetCell cell in row.Cells)
                    {
                        string text = cell.Value.ToString();
                        budget.Consume(text, cancellationToken);
                        yield return new(text, sheet.Index, sheet.Name, Array.AsReadOnly(new[]
                        {
                            new DocumentTextPathSegment(DocumentTextNodeKind.Sheet, sheet.Index),
                            new DocumentTextPathSegment(DocumentTextNodeKind.Row, row.RowIndex),
                            new DocumentTextPathSegment(DocumentTextNodeKind.Cell, cell.ColumnIndex)
                        }), DocumentTextStructure.Declared);
                    }
                }
            }
        }
    }
}
