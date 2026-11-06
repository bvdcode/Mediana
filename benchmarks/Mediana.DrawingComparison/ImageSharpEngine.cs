// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;

namespace Mediana.DrawingComparison
{
    internal class ImageSharpEngine : IDrawingEngine
    {
        private readonly FontFamily family;
        private readonly FontStyle style;
        internal ImageSharpEngine(ReadOnlyMemory<byte> bytes)
        {
            using MemoryStream stream = new(bytes.ToArray(), false);
            family = new FontCollection().Add(stream);
            style = family.GetAvailableStyles().Span[0];
        }
        public IRenderedImage Render(ComparisonScene scene)
        {
            Image<Rgba32> image = new(scene.Width, scene.Height);
            bool completed = false;
            try
            {
                using (DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(image.Configuration, new() { TextContrast = 0 }))
                {
                    scene.Draw(new ImageSharpTarget(canvas, family, style));
                }
                completed = true;
                return new ImageSharpImage(image);
            }
            finally { if (!completed) { image.Dispose(); } }
        }
    }
}
