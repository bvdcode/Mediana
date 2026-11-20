// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents
{
    /// <summary>An owned Unicode block in reading order. Path includes the block itself and identifies its ancestors.
    /// PartIndex is zero-based: a PDF page, presentation slide, worksheet, or Word section. PartName identifies the source package part when available.</summary>
    public record DocumentTextBlock(string Text, int PartIndex, string? PartName,
        IReadOnlyList<DocumentTextPathSegment> Path, DocumentTextStructure Structure)
    {
        public DocumentTextSourceKind SourceKind { get; init; }
        public DocumentHeaderFooterVariant? HeaderFooterVariant { get; init; }
        public DocumentTextNodeKind Kind => Path.Count == 0 ? DocumentTextNodeKind.Document : Path[^1].Kind;
    }
}
