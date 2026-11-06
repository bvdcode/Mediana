// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.Tests
{
    public class PreparedGlyphTests
    {
        [Theory]
        [InlineData("Canada1500", 'Я')]
        [InlineData("Aileron", 'Q')]
        public void PreparedGlyphsPreserveCoverageWithSubpixelPlacementAndChangingTransforms(string family, char character)
        {
            FontProgram program = BundledFontPrograms.GetProgram(family, false, false);
            TextRenderer renderer = new(new() { FontProgramResolver = _ => program });
            TrueTypeFont font = renderer.ResolveFont(new("Input"))!;
            int glyph = font.Glyph(character);
            foreach (float size in new float[] { 9, 23, 47 })
            {
                foreach (float fraction in new float[] { 0, .125f, .5f, .875f })
                {
                    using PixelBuffer expected = new(96, 96), actual = new(96, 96);
                    Matrix3x2 transform = Matrix3x2.CreateScale(size / font.Units, -size / font.Units) * Matrix3x2.CreateTranslation(10 + fraction, 70 + fraction);
                    new RasterCanvas(expected).FillPathAntialiased(font.Outline(glyph), new(23, 57, 139), transform);
                    renderer.DrawGlyphAntialiased(new(actual), font, glyph, new(23, 57, 139), transform);
                    int maximum = 0, total = 0;
                    for (int i = 3; i < actual.Pixels.Length; i += 4)
                    {
                        int difference = Math.Abs(actual.Pixels.Span[i] - expected.Pixels.Span[i]);
                        maximum = Math.Max(maximum, difference); total += difference;
                    }
                    Assert.True(maximum <= 2 && total <= 100, $"Coverage differs by {maximum} maximum and {total} total for {family}, {size}, {fraction}.");
                }
            }
        }
    }
}
