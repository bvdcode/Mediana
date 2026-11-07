// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Spreadsheets
{
    /// <summary>Stored cells in increasing column order. Missing coordinates are not synthesized; indexes are zero-based.</summary>
    public record SpreadsheetRow(int RowIndex, IReadOnlyList<SpreadsheetCell> Cells);
}
