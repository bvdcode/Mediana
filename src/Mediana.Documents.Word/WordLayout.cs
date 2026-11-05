// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    /// <summary>An immutable paginated document, reusable across independent concurrent page render operations.
    /// Page numbers are one-based. Layout owns font outlines and retains encoded pictures without copying them.</summary>
    public class WordLayout
    {
        internal WordLayout(WordPageSize size, IReadOnlyList<IReadOnlyList<WordPaint>> pages, IReadOnlyList<string> warnings)
        {
            PageSize = size;
            Pages = pages;
            Warnings = warnings;
        }
        public WordPageSize PageSize { get; }
        public int PageCount => Pages.Count;
        public IReadOnlyList<string> Warnings { get; }
        internal IReadOnlyList<IReadOnlyList<WordPaint>> Pages { get; }
    }
}
