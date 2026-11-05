// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal record GroupCommand(IPdfDrawCommand[] Commands, float Opacity, ColorBlendMode Blend,
        bool Isolated, bool Knockout, bool Gray) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, PdfDrawingContext context)
        {
            canvas.DrawLayer(layer =>
            {
                foreach (IPdfDrawCommand command in Commands)
                {
                    context.Token.ThrowIfCancellationRequested();
                    if (command is StateCommand or ClipCommand) { command.Draw(layer, context); }
                    else { layer.DrawObject(element => command.Draw(element, context), context.Token); }
                }
                if (Gray)
                {
                    Span<byte> pixels = layer.Buffer.Pixels.Span;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        if (i % (layer.Buffer.Width * 4) == 0) { context.Token.ThrowIfCancellationRequested(); }
                        byte value = PdfSoftMask.Luminosity(pixels[i], pixels[i + 1], pixels[i + 2]);
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                    }
                }
            }, Knockout, Opacity, Blend, Isolated, cancellationToken: context.Token);
        }
    }
}
