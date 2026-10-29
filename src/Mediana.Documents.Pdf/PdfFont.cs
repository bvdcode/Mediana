// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Svg;
namespace Mediana.Documents.Pdf
{
    internal record PdfFont(SvgTrueType Face, IReadOnlyDictionary<int, int> Glyphs, IReadOnlyDictionary<int, double> Widths, double DefaultWidth, bool Composite);
}
