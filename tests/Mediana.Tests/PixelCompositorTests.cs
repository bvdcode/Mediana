// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PixelCompositorTests
    {
        [Fact]
        public void NormalOnOpaqueMatchesNormalizedFloatForEveryChannelAndAlpha()
        {
            Span<byte> pixels = stackalloc byte[4];
            for (int alpha = 0; alpha < 256; alpha++)
            {
                float opacity = alpha / 255f;
                for (int source = 0; source < 256; source++)
                {
                    for (int backdrop = 0; backdrop < 256; backdrop++)
                    {
                        pixels.Fill((byte)backdrop);
                        pixels[3] = 255;
                        PixelCompositor.Blend(pixels, 0, new((byte)source, (byte)source, (byte)source, (byte)alpha), ColorBlendMode.Normal);
                        byte expected = (byte)MathF.Round(((opacity * (source / 255f)) + ((1 - opacity) * (backdrop / 255f))) * 255);
                        if (pixels[0] != expected || pixels[1] != expected || pixels[2] != expected || pixels[3] != 255)
                        { throw new InvalidDataException($"Source-over differs for source {source}, backdrop {backdrop}, alpha {alpha}."); }
                    }
                }
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void OpaqueSpansMatchPerPixelImagePaintingWithClipsAndLayerShapes(bool isolated, bool knockout)
        {
            using PixelBuffer actual = new(17, 13);
            using PixelBuffer expected = new(17, 13);
            using PixelBuffer source = new(1, 1);
            source.Clear(new(13, 159, 237));
            Draw(new(actual), false);
            Draw(new(expected), true);
            Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());

            void Draw(RasterCanvas canvas, bool image)
            {
                canvas.Buffer.Clear(new(30, 40, 50, 90));
                canvas.ClipPath(new VectorPath().Rectangle(1, 1, 14, 11).Rectangle(5, 4, 3, 2), System.Numerics.Matrix3x2.Identity, FillRule.EvenOdd);
                canvas.DrawLayer(layer => layer.DrawObject(element =>
                {
                    if (image)
                    {
                        element.DrawImage(source, System.Numerics.Matrix3x2.CreateScale(12, 9) * System.Numerics.Matrix3x2.CreateTranslation(2, 2));
                    }
                    else { element.FillPath(new VectorPath().Rectangle(2, 2, 12, 9), new(13, 159, 237), System.Numerics.Matrix3x2.Identity); }
                }), knockout, .7f, isolated: isolated);
            }
        }
    }
}
