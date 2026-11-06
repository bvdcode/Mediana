// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class CurveFlatteningTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CoverageChordsBoundTheCompleteBezierCurveIncludingCollinearTurns(bool cubic)
        {
            Random random = new(8349);
            for (int sample = 0; sample < 24; sample++)
            {
                Vector2 a = Point(), b = Point(), c = Point(), d = Point();
                if (sample == 0) { a = new(0, 0); b = new(90, 0); c = new(-60, 0); d = new(1, 0); }
                if (sample == 1) { d = a; }
                VectorPath path = new VectorPath().MoveTo(a.X, a.Y);
                if (cubic) { path.CubicTo(b.X, b.Y, c.X, c.Y, d.X, d.Y); }
                else { path.QuadraticTo(b.X, b.Y, d.X, d.Y); }
                List<Vector2> points = PathFlattener.FlattenCoverage(path, Matrix3x2.Identity, PathFlattener.AntialiasTolerance)[0].Points;
                for (int step = 0; step <= 2048; step++)
                {
                    float t = step / 2048f, inverse = 1 - t;
                    Vector2 at = cubic
                        ? a * (inverse * inverse * inverse) + b * (3 * inverse * inverse * t) + c * (3 * inverse * t * t) + d * (t * t * t)
                        : a * (inverse * inverse) + b * (2 * inverse * t) + d * (t * t);
                    float distance = float.PositiveInfinity;
                    for (int i = 1; i < points.Count; i++)
                    {
                        Vector2 chord = points[i] - points[i - 1];
                        float squared = chord.LengthSquared();
                        float projection = squared == 0 ? 0 : Math.Clamp(Vector2.Dot(at - points[i - 1], chord) / squared, 0, 1);
                        distance = Math.Min(distance, Vector2.DistanceSquared(at, points[i - 1] + projection * chord));
                    }
                    Assert.True(MathF.Sqrt(distance) <= PathFlattener.AntialiasTolerance + .0001f,
                        $"Curve {sample} at {t} leaves the chord by {MathF.Sqrt(distance)} pixels.");
                }

                Vector2 Point() => new(random.NextSingle() * 100, random.NextSingle() * 100);
            }
        }
    }
}
