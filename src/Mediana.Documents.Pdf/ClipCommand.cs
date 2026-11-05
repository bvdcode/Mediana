// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal record ClipCommand(VectorPath Path, FillRule Rule) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, PdfDrawingContext context) => canvas.ClipPath(Path, context.Device, Rule, context.Token);
    }
}
