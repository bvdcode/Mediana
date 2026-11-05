// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordParagraph(IReadOnlyList<WordInline> Content, WordParagraphFormat Format, WordRunFormat MarkFormat) : WordBlock;
}
