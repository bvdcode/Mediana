// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Tests
{
    public class DrawingResamplingTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        public void DownsamplingMatchesPremultipliedReferenceIncludingTransparentRgb(int factor)
        {
            using PixelBuffer source = new(7 * factor, 5 * factor);
            new Random(3847).NextBytes(source.Pixels.Span);
            for (int y = 0; y < factor; y++)
            {
                for (int x = 0; x < factor; x++) { source[x, y] = new(240, 80, 30, 0); }
            }
            using PixelBuffer result = source.Downsample(factor);
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    int r = 0, g = 0, b = 0, alpha = 0;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        for (int dx = 0; dx < factor; dx++)
                        {
                            Rgba32 color = source[x * factor + dx, y * factor + dy];
                            r += color.R * color.A; g += color.G * color.A; b += color.B * color.A; alpha += color.A;
                        }
                    }
                    Rgba32 expected = alpha == 0 ? default : new((byte)(r / alpha), (byte)(g / alpha), (byte)(b / alpha), (byte)(alpha / (factor * factor)));
                    Assert.Equal(expected, result[x, y]);
                }
            }
            Assert.Equal(default, result[0, 0]);
        }
    }
}
