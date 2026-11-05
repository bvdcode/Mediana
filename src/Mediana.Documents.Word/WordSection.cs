// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    /// <summary>Read-only section content with its own page geometry and inherited headers and footers.</summary>
    public class WordSection
    {
        internal WordSection(IReadOnlyList<WordBlock> blocks, WordPageGeometry geometry, WordMarginContent header,
            WordMarginContent footer, WordSectionStart start, bool distinctFirstPage, bool distinctEvenPages)
        {
            Blocks = blocks;
            Geometry = geometry;
            Header = header;
            Footer = footer;
            Start = start;
            DistinctFirstPage = distinctFirstPage;
            DistinctEvenPages = distinctEvenPages;
        }
        public WordSectionStart Start { get; }
        public WordPageSize PageSize => new(Geometry.Width, Geometry.Height);
        internal IReadOnlyList<WordBlock> Blocks { get; }
        internal WordPageGeometry Geometry { get; }
        internal WordMarginContent Header { get; }
        internal WordMarginContent Footer { get; }
        internal bool DistinctFirstPage { get; }
        internal bool DistinctEvenPages { get; }
    }
}
