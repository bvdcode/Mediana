// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Documents.Pdf
{
    internal record PdfType3Font(Matrix3x2 Matrix, PdfValue Resources, IReadOnlyDictionary<int, PdfValue> Glyphs,
        IReadOnlyDictionary<int, double> Widths) : PdfFont(Widths, 0, false)
    {
        internal override double WidthScale => Matrix.M11;
    }
}
