// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal interface IPdfDrawCommand
    {
        void Draw(RasterCanvas canvas, PdfDrawingContext context);
    }
}
