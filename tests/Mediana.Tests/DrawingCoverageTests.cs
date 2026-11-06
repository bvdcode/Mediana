// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class DrawingCoverageTests
    {
        [Theory]
        [InlineData(255)]
        [InlineData(137)]
        public void AntialiasedFillMatchesAnalyticRectangleArea(byte alpha)
        {
            using PixelBuffer pixels = new(9, 7);
            VectorPath path = new VectorPath().Rectangle(.25f, .5f, 3.5f, 2.75f);
            new RasterCanvas(pixels).FillPathAntialiased(path, new(23, 57, 139, alpha), Matrix3x2.Identity);
            for (int y = 0; y < pixels.Height; y++)
            {
                for (int x = 0; x < pixels.Width; x++)
                {
                    float width = Math.Max(0, Math.Min(x + 1, 3.75f) - Math.Max(x, .25f));
                    float height = Math.Max(0, Math.Min(y + 1, 3.25f) - Math.Max(y, .5f));
                    byte coverage = (byte)MathF.Round(alpha * width * height);
                    Assert.Equal(coverage, pixels[x, y].A);
                    if (coverage != 0) { Assert.Equal(new(23, 57, 139, coverage), pixels[x, y]); }
                }
            }
        }

        [Fact]
        public void AntialiasedFillPreservesEvenOddHolesAndCancellation()
        {
            using PixelBuffer pixels = new(7, 7);
            RasterCanvas canvas = new(pixels);
            VectorPath path = new VectorPath().Rectangle(.5f, .5f, 6, 6).Rectangle(2, 2, 3, 3);
            canvas.FillPathAntialiased(path, new(23, 57, 139), Matrix3x2.Identity, FillRule.EvenOdd);
            Assert.Equal(64, pixels[0, 0].A);
            Assert.Equal(0, pixels[3, 3].A);
            Assert.Equal(255, pixels[1, 1].A);
            Assert.Throws<OperationCanceledException>(() => canvas.FillPathAntialiased(path, default, Matrix3x2.Identity, cancellationToken: new(true)));
        }
    }
}
