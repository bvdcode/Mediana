// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordBreak(bool Page, WordRunFormat Style) : WordInline(Style);
}
