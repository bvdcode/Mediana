// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal record FillCommand(VectorPath Path, Rgba32 Color, FillRule Rule, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, PdfDrawingContext context) => canvas.FillPath(Path, Color, context.Device, Rule, Blend, context.Token);
    }
}
