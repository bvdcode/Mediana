// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Documents.Pdf
{
    internal record StrokeCommand(VectorPath Path, Rgba32 Color, StrokeStyle Style, Matrix3x2 Pen, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, PdfDrawingContext context)
        {
            if (Matrix3x2.Invert(Pen, out Matrix3x2 inverse))
            {
                canvas.StrokePathAntialiased(Path.Transform(inverse), Color, Style, Pen * context.Device, Blend, context.Token);
            }
        }
    }
}
