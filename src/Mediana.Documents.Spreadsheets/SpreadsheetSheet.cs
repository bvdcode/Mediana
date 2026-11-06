// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Spreadsheets
{
    public record SpreadsheetSheet(int Index, string Name, SpreadsheetSheetVisibility Visibility, SpreadsheetSheetKind Kind);
}
