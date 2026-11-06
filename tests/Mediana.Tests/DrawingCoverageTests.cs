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
        [Fact]
        public void CurvedCoverageApproximatesTheAnalyticAreaOfAParabola()
        {
            const double width = 63.3, height = 55.7;
            using PixelBuffer pixels = new(66, 58);
            VectorPath path = new VectorPath().MoveTo(0, 0).QuadraticTo((float)width / 2, 0, (float)width, (float)height).LineTo((float)width, 0).Close();
            new RasterCanvas(pixels).FillPathAntialiased(path, new(23, 57, 139), Matrix3x2.Identity);
            double squared = 0;
            int edges = 0;
            for (int y = 0; y < pixels.Height; y++)
            {
                for (int x = 0; x < pixels.Width; x++)
                {
                    double left = Math.Clamp(x, 0, width), right = Math.Clamp(x + 1, 0, width);
                    double first = Math.Clamp(width * Math.Sqrt(y / height), left, right);
                    double last = Math.Clamp(width * Math.Sqrt((y + 1) / height), left, right);
                    double area = height * (last * last * last - first * first * first) / (3 * width * width) - y * (last - first) + right - last;
                    double expected = Math.Clamp(area, 0, 1) * 255;
                    double difference = Math.Abs(pixels[x, y].A - expected);
                    Assert.True(difference <= 4, $"Coverage error {difference} at ({x}, {y}).");
                    if (expected > 0 && expected < 255) { squared += difference * difference; edges++; }
                }
            }
            Assert.True(Math.Sqrt(squared / edges) <= 1.5);
        }

        [Theory]
        [InlineData(FillRule.NonZero, 255)]
        [InlineData(FillRule.EvenOdd, 128)]
        public void OverlappingContoursUseTheSelectedWindingRule(FillRule rule, byte expected)
        {
            using PixelBuffer pixels = new(1, 1);
            VectorPath path = new VectorPath().Rectangle(0, 0, .75f, 1).Rectangle(.25f, 0, .75f, 1);
            new RasterCanvas(pixels).FillPathAntialiased(path, new(23, 57, 139), Matrix3x2.Identity, rule);
            Assert.Equal(expected, pixels[0, 0].A);
        }

        [Theory]
        [InlineData(FillRule.NonZero)]
        [InlineData(FillRule.EvenOdd)]
        public void CrossingEdgesSplitThePixelIntoTheCorrectTriangleAreas(FillRule rule)
        {
            using PixelBuffer pixels = new(1, 1);
            VectorPath path = new VectorPath().MoveTo(0, 0).LineTo(1, 1).LineTo(0, 1).LineTo(1, 0).Close();
            new RasterCanvas(pixels).FillPathAntialiased(path, new(23, 57, 139), Matrix3x2.Identity, rule);
            Assert.Equal(128, pixels[0, 0].A);
        }

        [Fact]
        public void ShallowCurvedStrokeJoinsUseStableIntersectionEvents()
        {
            using PixelBuffer pixels = new(200, 200);
            VectorPath path = new();
            for (int i = 0; i < 20; i++)
            {
                float y = 200 * (i + .5f) / 20;
                path.MoveTo(0, y).CubicTo(50, y + 4, 150, y - 4, 200, y);
            }
            new RasterCanvas(pixels).StrokePathAntialiased(path, new(23, 57, 139, 190), new() { Width = 1.2f }, Matrix3x2.Identity);
            Assert.Contains(pixels.Pixels.ToArray(), value => value > 0);
        }

        [Theory]
        [InlineData(FillRule.NonZero)]
        [InlineData(FillRule.EvenOdd)]
        public void RowPartitionsPreserveHorizontalStepsInsideThePixel(FillRule rule)
        {
            using PixelBuffer pixels = new(64, 1);
            VectorPath path = new VectorPath().MoveTo(0, .75f).LineTo(0, 1).LineTo(20, 1)
                .LineTo(20, 0).LineTo(10, 0).LineTo(10, .75f).Close();
            for (int i = 0; i < 12; i++) { path.Rectangle(25 + i * 2, .1f, .75f, .8f); }
            new RasterCanvas(pixels).FillPathAntialiased(path, new(23, 57, 139), Matrix3x2.Identity, rule);
            Assert.Equal(64, pixels[5, 0].A);
            Assert.Equal(255, pixels[15, 0].A);
            Assert.Equal(0, pixels[22, 0].A);
        }

    }
}
