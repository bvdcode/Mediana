// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordFlowBlock(IReadOnlyList<WordLine> Lines, double Before, double After, bool PageBreakBefore,
        bool KeepTogether, bool KeepNext, bool WidowControl);
}
