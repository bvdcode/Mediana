// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class ScanlineRasterizerTests
    {
        [Theory]
        [InlineData(FillRule.NonZero)]
        [InlineData(FillRule.EvenOdd)]
        public void ActiveEdgesMatchFullEdgeReferenceWithTransformsAndClippedContours(FillRule rule)
        {
            const int width = 47, height = 39;
            Random random = new(59321);
            for (int sample = 0; sample < 80; sample++)
            {
                VectorPath path = new();
                for (int contour = 0; contour < 7; contour++)
                {
                    Vector2 start = Point();
                    path.MoveTo(start.X, start.Y);
                    for (int point = 0; point < 5; point++)
                    {
                        Vector2 next = Point();
                        path.LineTo(next.X, next.Y);
                    }
                    if (contour % 2 == 0) { path.Close(); }
                }
                Matrix3x2 transform = Matrix3x2.CreateScale(sample % 2 == 0 ? -1 : 1, .8f)
                    * Matrix3x2.CreateRotation(sample * .03f) * Matrix3x2.CreateTranslation(width / 2f, height / 2f);
                Compare(path, transform, rule, width, height);

                Vector2 Point() => new((random.NextSingle() - .5f) * width * 2, (random.NextSingle() - .5f) * height * 2);
            }
        }

        [Theory]
        [InlineData(FillRule.NonZero)]
        [InlineData(FillRule.EvenOdd)]
        public void CurvedStrokeCoverageMatchesFullEdgeReference(FillRule rule)
        {
            VectorPath path = new();
            for (int i = 0; i < 80; i++)
            {
                float y = 960 * (i + .5f) / 80;
                path.MoveTo(0, y).CubicTo(320, y + 19.2f, 960, y - 19.2f, 1280, y);
            }
            VectorPath outline = PathStroker.Outline(path, new() { Width = 1.2f }, Matrix3x2.Identity, default);
            Compare(outline, Matrix3x2.Identity, rule, 1280, 960);
        }

        private static void Compare(VectorPath path, Matrix3x2 transform, FillRule rule, int width, int height)
        {
            List<ScanlineEdge> edges = [];
            foreach (PathContour contour in PathFlattener.Flatten(path, transform))
            {
                for (int i = 0; i < contour.Points.Count; i++)
                {
                    Vector2 a = contour.Points[i], b = contour.Points[(i + 1) % contour.Points.Count];
                    if (a.Y == b.Y) { continue; }
                    Vector2 low = a.Y < b.Y ? a : b, high = a.Y < b.Y ? b : a;
                    edges.Add(new(low.X, low.Y, high.Y, (high.X - low.X) / (high.Y - low.Y), a.Y < b.Y ? 1 : -1));
                }
            }
            float[] expected = new float[width * height], actual = new float[width * height];
            bool[] binary = new bool[width * height], actualBinary = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                List<float> divisions = Enumerable.Range(0, 9).Select(i => y + i / 8f).ToList();
                foreach (ScanlineEdge edge in edges)
                {
                    if (edge.MinimumY > y && edge.MinimumY < y + 1) { divisions.Add(edge.MinimumY); }
                    if (edge.MaximumY > y && edge.MaximumY < y + 1) { divisions.Add(edge.MaximumY); }
                }
                divisions.Sort();
                for (int i = 1; i < divisions.Count; i++)
                {
                    float start = divisions[i - 1], end = divisions[i];
                    if (end > start) { Integrate((start + end) / 2, y, end - start, false); }
                }
                for (int x = 0; x < width; x++) { expected[y * width + x] = Math.Min(1, expected[y * width + x]); }
                Integrate(y + .5f, y, 1, true);
            }
            ScanlineRasterizer.PaintCoverage(path, transform, rule, width, height, (x, y, value) => actual[y * width + x] = value, default);
            ScanlineRasterizer.Paint(path, transform, rule, width, height, (y, left, right) => Array.Fill(actualBinary, true, y * width + left, right - left), default);
            Assert.Equal(expected, actual);
            Assert.Equal(binary, actualBinary);

            bool Inside(int value) => rule switch
            {
                FillRule.NonZero => value != 0,
                FillRule.EvenOdd => value % 2 != 0,
                _ => throw new ArgumentOutOfRangeException(nameof(rule))
            };

            void Integrate(float position, int y, float interval, bool center)
            {
                List<ScanlineCrossing> crossings = [];
                foreach (ScanlineEdge edge in edges)
                {
                    if (position >= edge.MinimumY && position < edge.MaximumY)
                    { crossings.Add(new(edge.X + (position - edge.MinimumY) * edge.Slope, edge.Winding)); }
                }
                crossings.Sort((a, b) => a.X.CompareTo(b.X));
                int winding = 0;
                float left = 0;
                foreach ((float x, int direction) in crossings)
                {
                    bool inside = Inside(winding);
                    winding += direction;
                    bool next = Inside(winding);
                    if (!inside && next) { left = x; }
                    else if (inside && !next)
                    {
                        if (center)
                        {
                            int first = (int)Math.Clamp(MathF.Ceiling(left - .5f), 0, width);
                            int last = (int)Math.Clamp(MathF.Ceiling(x - .5f), 0, width);
                            Array.Fill(binary, true, y * width + first, last - first);
                        }
                        else
                        {
                            float first = Math.Clamp(left, 0, width), last = Math.Clamp(x, 0, width);
                            for (int column = (int)MathF.Floor(first); column < MathF.Ceiling(last); column++)
                            { expected[y * width + column] += (Math.Min(last, column + 1) - Math.Max(first, column)) * interval; }
                        }
                    }
                }
            }
        }
    }
}
