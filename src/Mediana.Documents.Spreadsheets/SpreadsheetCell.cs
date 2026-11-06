// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Spreadsheets
{
    /// <summary>A stored cell with a zero-based column index. A blank cached value differs from a missing formula cache.</summary>
    public record SpreadsheetCell(int ColumnIndex, SpreadsheetValue Value, int StyleIndex = 0, SpreadsheetFormula? Formula = null);
}
