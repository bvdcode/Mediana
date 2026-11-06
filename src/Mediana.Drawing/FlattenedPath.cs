// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    internal class FlattenedPath
    {
        private readonly ScanlineEdge[] edges;
        internal ReadOnlySpan<ScanlineEdge> Edges => edges;
        internal int PointCount { get; }
        internal float Left { get; }
        internal float Right { get; }
        internal float Top { get; }
        internal float Bottom { get; }

        internal FlattenedPath(VectorPath path, Matrix3x2 transform, CancellationToken token, float tolerance = PathFlattener.AntialiasTolerance, bool antialiased = true)
        {
            List<ScanlineEdge> result = [];
            float left = float.PositiveInfinity, right = float.NegativeInfinity, top = float.PositiveInfinity, bottom = float.NegativeInfinity;
            List<PathContour> contours = antialiased
                ? PathFlattener.FlattenCoverage(path, transform, tolerance, token)
                : PathFlattener.Flatten(path, transform, tolerance, token);
            foreach (PathContour contour in contours)
            {
                List<Vector2> points = contour.Points;
                PointCount += points.Count;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 a = points[i], b = points[(i + 1) % points.Count];
                    if (!float.IsFinite(a.X) || !float.IsFinite(a.Y)) { throw new ArgumentOutOfRangeException(nameof(path), "Transformed path coordinates must be finite."); }
                    left = Math.Min(left, a.X); right = Math.Max(right, a.X);
                    top = Math.Min(top, a.Y); bottom = Math.Max(bottom, a.Y);
                    if (a.Y != b.Y)
                    {
                        Vector2 low = a.Y < b.Y ? a : b, high = a.Y < b.Y ? b : a;
                        result.Add(new(low.X, low.Y, high.Y, (high.X - low.X) / (high.Y - low.Y), a.Y < b.Y ? 1 : -1, result.Count));
                    }
                }
            }
            Left = PointCount == 0 ? 0 : left; Right = PointCount == 0 ? 0 : right;
            Top = PointCount == 0 ? 0 : top; Bottom = PointCount == 0 ? 0 : bottom;
            if (result.Count > ScanlineEdgeTable.ActiveEdgeThreshold) { result.Sort(static (a, b) => a.MinimumY.CompareTo(b.MinimumY)); }
            edges = result.ToArray();
        }
    }
}
