// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;
using System.Threading;

namespace Mediana.Documents.Pdf
{
    internal record ImageCommand(PixelBuffer Image, Matrix3x2 Placement, float Opacity, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken) => canvas.DrawImage(Image, Placement * device, Opacity, Blend, cancellationToken);
    }
}
