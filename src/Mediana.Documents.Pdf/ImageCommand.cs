// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Video;
using System.Numerics;

namespace Mediana.Documents.Pdf
{
    internal record ImageCommand(VideoFrame Image, Matrix3x2 Placement, float Opacity, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, PdfDrawingContext context) => canvas.DrawImage(Image, Placement * context.Device, Opacity, Blend, context.Token);
    }
}
