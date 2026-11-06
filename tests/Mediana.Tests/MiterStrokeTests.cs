// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class MiterStrokeTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ClosedRectangleMiterStrokePreservesItsHoleAndOrientation(bool reverse)
        {
            using PixelBuffer actual = new(32, 32), expected = new(32, 32);
            VectorPath center = reverse
                ? new VectorPath().MoveTo(6, 6).LineTo(6, 26).LineTo(26, 26).LineTo(26, 6).Close()
                : new VectorPath().Rectangle(6, 6, 20, 20);
            new RasterCanvas(actual).StrokePathAntialiased(center, new(23, 57, 139, 137), new() { Width = 4 }, Matrix3x2.Identity);
            new RasterCanvas(expected).FillPathAntialiased(new VectorPath().Rectangle(4, 4, 24, 24).Rectangle(8, 8, 16, 16),
                new(23, 57, 139, 137), Matrix3x2.Identity, FillRule.EvenOdd);
            Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
        }

        [Fact]
        public void OpenPolylineMiterStrokeMatchesItsAnalyticBoundary()
        {
            using PixelBuffer actual = new(32, 32), expected = new(32, 32);
            new RasterCanvas(actual).StrokePathAntialiased(new VectorPath().MoveTo(5, 5).LineTo(25, 5).LineTo(25, 25),
                new(23, 57, 139, 137), new() { Width = 4 }, Matrix3x2.Identity);
            new RasterCanvas(expected).FillPathAntialiased(new VectorPath().MoveTo(5, 3).LineTo(27, 3).LineTo(27, 25).LineTo(23, 25).LineTo(23, 7).LineTo(5, 7).Close(),
                new(23, 57, 139, 137), Matrix3x2.Identity);
            Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
        }
        [Fact]
        public void MiteredBoundariesMatchTheUnionOfSegmentsAcrossTurnsAndLoops()
        {
            Random random = new(593);
            for (int sample = 0; sample < 40; sample++)
            {
                VectorPath path = new VectorPath().MoveTo(10, 10);
                for (int i = 0; i < 5; i++) { path.LineTo(5 + random.NextSingle() * 50, 5 + random.NextSingle() * 50); }
                if (sample % 2 == 0) { path.Close(); }
                StrokeStyle style = new() { Width = 1 + sample % 7, MiterLimit = 1 + sample % 10 };
                using PixelBuffer actual = new(64, 64), expected = new(64, 64);
                new RasterCanvas(actual).StrokePathAntialiased(path, new(23, 57, 139), style, Matrix3x2.Identity);
                VectorPath union = PathStroker.Outline(path, style, Matrix3x2.Identity, default, PathFlattener.AntialiasTolerance);
                new RasterCanvas(expected).FillPathAntialiased(union, new(23, 57, 139), Matrix3x2.Identity);
                for (int i = 3; i < actual.Pixels.Length; i += 4)
                {
                    int difference = Math.Abs(actual.Pixels.Span[i] - expected.Pixels.Span[i]);
                    Assert.True(difference <= 1, $"Stroke boundary differs by {difference} at sample {sample}, {i / 4 % 64}, {i / 4 / 64}.");
                }
            }
        }

    }
}
