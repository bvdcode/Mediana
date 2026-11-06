// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    public partial class PdfDocument
    {
        /// <summary>Extracts Unicode blocks without rasterization or font-program loading. Tagged structure is declared;
        /// geometric line ordering for untagged content is reconstructed.</summary>
        public IEnumerable<DocumentTextBlock> ReadText(DocumentTextOptions? options = null,CancellationToken cancellationToken = default)
        {
            options ??= new(); options.Validate();
            PdfDocumentText source;
            lock (_sync) { source = new(RequireOpen(),_pages.ToArray(),_optionalContent,options,cancellationToken); }
            using IEnumerator<DocumentTextBlock> enumerator = source.Read().GetEnumerator();
            while (true)
            {
                DocumentTextBlock block;
                lock (_sync)
                {
                    RequireOpen(); cancellationToken.ThrowIfCancellationRequested();
                    if (!enumerator.MoveNext()) { yield break; }
                    block = enumerator.Current;
                }
                yield return block;
            }
        }
    }
}
