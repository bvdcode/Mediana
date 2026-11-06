// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents
{
    /// <summary>An owned Unicode block in reading order. Path includes the block itself and identifies its ancestors.
    /// PartIndex is zero-based: a PDF page, presentation slide, worksheet, or document body part.</summary>
    public record DocumentTextBlock(string Text, int PartIndex, string? PartName,
        IReadOnlyList<DocumentTextPathSegment> Path, DocumentTextStructure Structure)
    {
        public DocumentTextNodeKind Kind => Path.Count == 0 ? DocumentTextNodeKind.Document : Path[^1].Kind;
    }
}
