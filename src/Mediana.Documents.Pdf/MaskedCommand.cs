// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal record MaskedCommand(IPdfDrawCommand Command, PdfSoftMask Mask) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, PdfDrawingContext context) =>
            canvas.DrawLayer(layer => Command.Draw(layer, context), isolated: false,
                alphaMask: context.Mask(Mask), cancellationToken: context.Token);
    }
}
