// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    /// <summary>An immutable paginated document, reusable across independent concurrent page render operations.
    /// Page numbers are one-based. Layout owns font outlines and retains encoded pictures without copying them.</summary>
    public class WordLayout
    {
        internal WordLayout(IReadOnlyList<WordLayoutPage> pages, IReadOnlyList<string> warnings)
        {
            Pages = pages;
            Warnings = warnings;
        }
        /// <summary>Gets the first page's size. Use GetPageSize for a selected page.</summary>
        public WordPageSize PageSize => Pages[0].Size;
        public int PageCount => Pages.Count;
        public IReadOnlyList<string> Warnings { get; }
        internal IReadOnlyList<WordLayoutPage> Pages { get; }

        public WordPageSize GetPageSize(int pageNumber)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pageNumber, PageCount);
            return Pages[pageNumber - 1].Size;
        }
    }
}
