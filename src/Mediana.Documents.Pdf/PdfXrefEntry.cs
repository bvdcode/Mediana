// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal readonly record struct PdfXrefEntry(int Kind, int Offset, int GenerationOrIndex);
}
