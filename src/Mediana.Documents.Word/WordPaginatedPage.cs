// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordPaginatedPage(WordSection Section, bool FirstInSection, bool Blank, IReadOnlyList<WordPaint> Paint);
}
