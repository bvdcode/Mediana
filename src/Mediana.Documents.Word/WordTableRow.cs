// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordTableRow(IReadOnlyList<WordTableCell> Cells, double MinimumHeight, bool ExactHeight, bool KeepTogether);
}
