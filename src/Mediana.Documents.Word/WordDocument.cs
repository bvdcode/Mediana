// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    /// <summary>An independent, read-only Word document. Encoded package storage is not retained.</summary>
    public class WordDocument
    {
        internal WordDocument(IReadOnlyList<WordBlock> blocks, WordPageGeometry geometry,
            WordMarginContent header, WordMarginContent footer, bool distinctFirstPage, bool distinctEvenPages, IReadOnlyList<string> warnings)
        {
            Blocks = blocks;
            Geometry = geometry;
            Header = header;
            Footer = footer;
            DistinctFirstPage = distinctFirstPage;
            DistinctEvenPages = distinctEvenPages;
            Warnings = warnings;
        }

        public WordPageSize PageSize => new(Geometry.Width, Geometry.Height);
        public IReadOnlyList<string> Warnings { get; }
        internal IReadOnlyList<WordBlock> Blocks { get; }
        internal WordPageGeometry Geometry { get; }
        internal WordMarginContent Header { get; }
        internal WordMarginContent Footer { get; }
        internal bool DistinctFirstPage { get; }
        internal bool DistinctEvenPages { get; }
    }
}
