// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mediana.DrawingComparison
{
    internal class ImageSharpImage(Image<Rgba32> image) : IRenderedImage
    {
        public void CopyPixelsTo(Span<byte> destination) => image.CopyPixelDataTo(destination);
        public void Dispose() => image.Dispose();
    }
}
