// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
namespace Mediana.Documents.Pdf
{
    internal record PdfPageSource(PdfValue Dictionary, PdfValue Resources, PdfPageInfo Info, Matrix3x2 Transform);
}
