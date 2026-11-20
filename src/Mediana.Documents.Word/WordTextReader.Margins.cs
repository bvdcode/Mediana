// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;

namespace Mediana.Documents.Word
{
    public partial class WordTextReader
    {
        internal static IEnumerable<DocumentTextBlock> ReadMargin(OpenXmlPackage package, string part, int section,
            DocumentTextSourceKind kind, DocumentHeaderFooterVariant variant, DocumentTextOptions options, DocumentTextBudget budget, CancellationToken token)
        {
            (string root, DocumentTextNodeKind node) = kind switch
            {
                DocumentTextSourceKind.Header => ("hdr", DocumentTextNodeKind.Header),
                DocumentTextSourceKind.Footer => ("ftr", DocumentTextNodeKind.Footer),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            foreach (DocumentTextBlock block in ReadPart(package, part, root, section, new(node, (int)variant), options, budget, token))
            { yield return block with { SourceKind = kind, HeaderFooterVariant = variant }; }
        }
    }
}
