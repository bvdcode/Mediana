// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal class PdfBudget(PdfReaderOptions options)
    {
        private int _values;
        private long _decoded;
        internal PdfReaderOptions Options { get; } = options;
        internal void Value(int depth, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (depth > Options.MaximumDepth || ++_values > Options.MaximumValues)
            { throw new InvalidDataException("PDF structural complexity exceeds its limit."); }
        }
        internal void Decoded(int count)
        {
            _decoded = checked(_decoded + count);
            if (_decoded > Options.MaximumDecodedBytes) { throw new InvalidDataException("PDF decoded streams exceed their byte limit."); }
        }
    }
}
