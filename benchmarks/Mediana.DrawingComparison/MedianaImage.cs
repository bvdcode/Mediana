// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.DrawingComparison
{
    internal class MedianaImage(PixelBuffer pixels) : IRenderedImage
    {
        public void CopyPixelsTo(Span<byte> destination) => pixels.Pixels.Span.CopyTo(destination);
        public void Dispose() => pixels.Dispose();
    }
}
