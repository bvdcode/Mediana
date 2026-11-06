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
                foreach (float fraction in new float[] { 0, .001f, .12345f, .125f, .5f, .875f })
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

        [Theory]
        [InlineData(0)]
        [InlineData(137)]
        [InlineData(255)]
        public void CachedGlyphCoveragePreservesClippingAndKnockoutShapeWhenColorChanges(byte alpha)
        {
            TextRenderer renderer = new();
            TrueTypeFont font = renderer.ResolveFont(new("Canada1500"))!;
            int glyph = font.Glyph('Я');
            Matrix3x2 transform = Matrix3x2.CreateScale(39 / font.Units, -39 / font.Units)
                * Matrix3x2.CreateTranslation(10.125f, 52.5f);
            using PixelBuffer actual = new(64, 64), expected = new(64, 64);
            renderer.DrawGlyphAntialiased(new(actual), font, glyph, new(100, 20, 30), transform);
            actual.Clear(new(230, 220, 200)); expected.Clear(new(230, 220, 200));
            Draw(new(actual), canvas => renderer.DrawGlyphAntialiased(canvas, font, glyph, new(23, 57, 139, alpha), transform));
            Draw(new(expected), canvas => canvas.FillPathAntialiased(font.Outline(glyph), new(23, 57, 139, alpha), transform));
            for (int i = 0; i < actual.Pixels.Length; i++)
            {
                Assert.True(Math.Abs(actual.Pixels.Span[i] - expected.Pixels.Span[i]) <= 1,
                    $"Clipped knockout glyph differs at byte {i} for alpha {alpha}.");
            }

            static void Draw(RasterCanvas canvas, Action<RasterCanvas> glyph)
            {
                canvas.ClipPath(new VectorPath().Rectangle(19, 5, 30, 43), Matrix3x2.Identity);
                canvas.DrawLayer(layer =>
                {
                    layer.DrawObject(item => item.FillPath(new VectorPath().Rectangle(10, 10, 40, 40), new(200, 40, 20, 190), Matrix3x2.Identity));
                    layer.DrawObject(glyph);
                }, knockout: true, opacity: .7f, blendMode: ColorBlendMode.Multiply, isolated: false);
            }
        }
    }
}
