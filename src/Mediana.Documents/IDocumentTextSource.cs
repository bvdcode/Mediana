// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents
{
    /// <summary>Single-consumer lazy text enumeration. Breaking enumeration disposes active readers; no rasterization is required.</summary>
    public interface IDocumentTextSource
    {
        IEnumerable<DocumentTextBlock> ReadText(DocumentTextOptions? options = null, CancellationToken cancellationToken = default);
    }
}
