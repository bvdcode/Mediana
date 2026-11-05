// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal record WordTableCell(IReadOnlyList<WordBlock> Blocks, int Span, double Left, double Right, double Top, double Bottom,
        Rgba32? Shading, WordBorders Borders);
}
