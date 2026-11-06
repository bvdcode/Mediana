// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    /// <summary>An independent, read-only Word document. Encoded package storage is not retained.</summary>
    public partial class WordDocument : IDocumentTextSource
    {
        internal WordDocument(IReadOnlyList<WordSection> sections, IReadOnlyList<string> warnings)
        {
            Sections = sections;
            Warnings = warnings;
        }
        /// <summary>Gets the first section's page size. Paginated pages can have different dimensions.</summary>
        public WordPageSize PageSize => Sections[0].PageSize;
        public IReadOnlyList<WordSection> Sections { get; }
        public IReadOnlyList<string> Warnings { get; }
    }
}
