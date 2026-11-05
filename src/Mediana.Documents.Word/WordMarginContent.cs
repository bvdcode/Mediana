// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordMarginContent(IReadOnlyList<WordBlock> Default, IReadOnlyList<WordBlock> First, IReadOnlyList<WordBlock> Even);
}
