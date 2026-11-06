// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.DrawingComparison
{
    internal class MedianaEngine(ReadOnlyMemory<byte> data) : IDrawingEngine
    {
        private readonly FontProgram program = new(data);
        public IRenderedImage Render(ComparisonScene scene)
        {
            PixelBuffer pixels = new(scene.PixelWidth, scene.PixelHeight);
            bool completed = false;
            try
            {
                scene.Draw(new MedianaTarget(new(pixels), program));
                completed = true;
                return new MedianaImage(pixels);
            }
            finally { if (!completed) { pixels.Dispose(); } }
        }
    }
}
