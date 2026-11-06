// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal struct ScanlineEdgeTable
    {
        internal const int ActiveEdgeThreshold = 32;
        private readonly FlattenedPath geometry;
        private readonly Vector2 offset;
        private readonly List<ScanlineEdge> active;
        private int next;
        internal int Left { get; }
        internal int Right { get; }
        internal int Top { get; }
        internal int Bottom { get; }
        internal ReadOnlySpan<ScanlineEdge> Active => CollectionsMarshal.AsSpan(active);

        internal ScanlineEdgeTable(VectorPath path, Matrix3x2 transform, int width, int height, CancellationToken token, float tolerance = .2f, bool antialiased = false)
            : this(new FlattenedPath(path, transform, token, tolerance, antialiased), Vector2.Zero, width, height)
        {
        }

        internal ScanlineEdgeTable(FlattenedPath geometry, Vector2 offset, int width, int height)
        {
            this.geometry = geometry;
            this.offset = offset;
            Left = Math.Max(0, (int)Math.Clamp(MathF.Floor(geometry.Left + offset.X), 0, width) - 1);
            Right = Math.Min(width, (int)Math.Clamp(MathF.Ceiling(geometry.Right + offset.X), 0, width) + 1);
            Top = (int)Math.Clamp(MathF.Floor(geometry.Top + offset.Y), 0, height);
            Bottom = (int)Math.Clamp(MathF.Ceiling(geometry.Bottom + offset.Y), 0, height);
            active = new(Math.Min(geometry.Edges.Length, ActiveEdgeThreshold));
            if (geometry.Edges.Length <= ActiveEdgeThreshold)
            {
                foreach (ScanlineEdge edge in geometry.Edges) { active.Add(Translate(edge)); }
            }
        }

        internal void SelectRow(int y)
        {
            if (geometry.Edges.Length <= ActiveEdgeThreshold) { return; }
            int retained = 0;
            for (int i = 0; i < active.Count; i++)
            {
                ScanlineEdge edge = active[i];
                if (edge.MaximumY > y) { active[retained++] = edge; }
            }
            active.RemoveRange(retained, active.Count - retained);
            bool added = false;
            while (next < geometry.Edges.Length && geometry.Edges[next].MinimumY + offset.Y <= y + 1f)
            {
                ScanlineEdge edge = Translate(geometry.Edges[next++]);
                if (edge.MaximumY > y) { active.Add(edge); added = true; }
            }
            if (added) { active.Sort(static (a, b) => a.Order.CompareTo(b.Order)); }
        }

        internal bool GetRowBounds(int y, out int left, out int right)
        {
            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            foreach (ScanlineEdge edge in Active)
            {
                double top = Math.Max(y, edge.MinimumY), bottom = Math.Min((double)y + 1, edge.MaximumY);
                if (top >= bottom) { continue; }
                double first = edge.X + (top - edge.MinimumY) * edge.Slope;
                double last = edge.X + (bottom - edge.MinimumY) * edge.Slope;
                minimum = Math.Min(minimum, Math.Min(first, last));
                maximum = Math.Max(maximum, Math.Max(first, last));
            }
            left = (int)Math.Clamp(Math.Floor(minimum), Left, Right);
            right = (int)Math.Clamp(Math.Ceiling(maximum), Left, Right);
            return right > left;
        }

        private ScanlineEdge Translate(ScanlineEdge edge) => edge with
        {
            X = edge.X + offset.X,
            MinimumY = edge.MinimumY + offset.Y,
            MaximumY = edge.MaximumY + offset.Y
        };
    }
}
