// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>


using Mediana.Drawing.Fonts;

namespace Mediana.Documents.Pdf
{
    internal record PdfFont(TrueTypeFont Face, IReadOnlyDictionary<int, int> Glyphs, IReadOnlyDictionary<int, double> Widths, double DefaultWidth, bool Composite);
}
