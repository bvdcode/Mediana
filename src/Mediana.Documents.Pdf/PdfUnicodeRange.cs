// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal readonly record struct PdfUnicodeRange(int First, int Last, byte[] Destination);
}
