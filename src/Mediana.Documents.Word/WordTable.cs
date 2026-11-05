// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordTable(IReadOnlyList<double> Columns, IReadOnlyList<WordTableRow> Rows, double Indent, WordAlignment Alignment) : WordBlock;
}
