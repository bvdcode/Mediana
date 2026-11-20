// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordMarginContent(IReadOnlyList<WordBlock> Default, IReadOnlyList<WordBlock> First, IReadOnlyList<WordBlock> Even)
    {
        internal IReadOnlyDictionary<DocumentHeaderFooterVariant, string> PartNames { get; init; } = new Dictionary<DocumentHeaderFooterVariant, string>();
        internal IReadOnlyList<WordBlock> GetBlocks(DocumentHeaderFooterVariant variant) => variant switch
        {
            DocumentHeaderFooterVariant.Default => Default,
            DocumentHeaderFooterVariant.First => First,
            DocumentHeaderFooterVariant.Even => Even,
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
    }
}
