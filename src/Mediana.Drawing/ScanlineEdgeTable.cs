// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal struct ScanlineEdgeTable
    {
        private const int ActiveEdgeThreshold = 32;
        private readonly List<ScanlineEdge> edges = [];
        private readonly List<ScanlineEdge>? active;
        private int next;
        internal int Left { get; }
        internal int Right { get; }
        internal int Top { get; }
        internal int Bottom { get; }
        internal ReadOnlySpan<ScanlineEdge> Active => CollectionsMarshal.AsSpan(active ?? edges);

        internal ScanlineEdgeTable(VectorPath path, Matrix3x2 transform, int width, int height, CancellationToken token, float tolerance = .2f)
            : this(PathFlattener.Flatten(path, transform, tolerance, token), Vector2.Zero, width, height)
        {
        }

        internal ScanlineEdgeTable(IReadOnlyList<PathContour> contours, Vector2 offset, int width, int height)
        {
            float left = width, right = 0, top = height, bottom = 0;
            foreach (PathContour contour in contours)
            {
                List<Vector2> points = contour.Points;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 a = points[i] + offset, b = points[(i + 1) % points.Count] + offset;
                    if (!float.IsFinite(a.X) || !float.IsFinite(a.Y)) { throw new ArgumentOutOfRangeException(nameof(contours), "Transformed path coordinates must be finite."); }
                    left = Math.Min(left, a.X); right = Math.Max(right, a.X);
                    top = Math.Min(top, a.Y); bottom = Math.Max(bottom, a.Y);
                    if (a.Y != b.Y)
                    {
                        Vector2 low = a.Y < b.Y ? a : b;
                        Vector2 high = a.Y < b.Y ? b : a;
                        edges.Add(new(low.X, low.Y, high.Y, (high.X - low.X) / (high.Y - low.Y), a.Y < b.Y ? 1 : -1, edges.Count));
                    }
                }
            }
            Left = Math.Max(0, (int)Math.Clamp(MathF.Floor(left), 0, width) - 1);
            Right = Math.Min(width, (int)Math.Clamp(MathF.Ceiling(right), 0, width) + 1);
            Top = (int)Math.Clamp(MathF.Floor(top), 0, height);
            Bottom = (int)Math.Clamp(MathF.Ceiling(bottom), 0, height);
            if (edges.Count > ActiveEdgeThreshold)
            {
                active = [];
                edges.Sort(static (a, b) => a.MinimumY.CompareTo(b.MinimumY));
            }
        }

        internal void SelectRow(int y)
        {
            if (active is null) { return; }
            int retained = 0;
            for (int i = 0; i < active.Count; i++)
            {
                ScanlineEdge edge = active[i];
                if (edge.MaximumY > y) { active[retained++] = edge; }
            }
            active.RemoveRange(retained, active.Count - retained);
            bool added = false;
            // A subrow midpoint between adjacent floats can round onto the next row.
            while (next < edges.Count && edges[next].MinimumY <= y + 1f)
            {
                ScanlineEdge edge = edges[next++];
                if (edge.MaximumY > y) { active.Add(edge); added = true; }
            }
            if (added) { active.Sort(static (a, b) => a.Order.CompareTo(b.Order)); }
        }
    }
}
