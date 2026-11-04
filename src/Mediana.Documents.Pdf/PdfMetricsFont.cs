// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal record PdfMetricsFont(IReadOnlyDictionary<int, double> Widths, double DefaultWidth, bool Composite)
        : PdfFont(Widths, DefaultWidth, Composite)
    {
        internal override double WidthScale => 0.001;
    }
}
