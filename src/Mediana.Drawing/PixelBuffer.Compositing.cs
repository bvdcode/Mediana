// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Threading;

namespace Mediana.Drawing
{
    public partial class PixelBuffer
    {
        /// <summary>Composites these straight-alpha pixels over a constant background in place.</summary>
        public void CompositeBackground(Rgba32 background, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> pixels = Pixels.Span;
            for (int y = 0; y < Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < Width; x++)
                {
                    int offset = (y * Width + x) * 4;
                    if (pixels[offset + 3] == 255) { continue; }
                    Rgba32 source = new(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
                    pixels[offset] = background.R; pixels[offset + 1] = background.G;
                    pixels[offset + 2] = background.B; pixels[offset + 3] = background.A;
                    PixelCompositor.Blend(pixels, offset, source, ColorBlendMode.Normal);
                }
            }
        }
    }
}
