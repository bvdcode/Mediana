// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal record PdfObject(PdfValue Value, ReadOnlyMemory<byte> Stream, bool HasStream = false);
}
