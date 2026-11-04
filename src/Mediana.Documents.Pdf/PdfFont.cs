// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>


namespace Mediana.Documents.Pdf
{
    internal abstract record PdfFont(IReadOnlyDictionary<int, double> Widths, double DefaultWidth, bool Composite)
    {
        internal abstract double WidthScale { get; }
    }
}
