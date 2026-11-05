// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordLayoutPage(WordPageSize Size, IReadOnlyList<WordPaint> Paint);
}
