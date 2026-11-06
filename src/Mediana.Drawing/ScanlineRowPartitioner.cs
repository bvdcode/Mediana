// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal class ScanlineRowPartitioner
    {
        private readonly List<ProjectedScanlineEdge> projected = [];
        private readonly List<ScanlineEdge> edges = [];
        private readonly List<int> ends = [];
        private readonly Dictionary<double, int> events = [];
        internal ReadOnlySpan<ScanlineEdge> Edges => CollectionsMarshal.AsSpan(edges);
        internal ReadOnlySpan<int> Ends => CollectionsMarshal.AsSpan(ends);

        internal void SelectRow(ReadOnlySpan<ScanlineEdge> active, int y, FillRule rule)
        {
            projected.Clear(); edges.Clear(); ends.Clear(); events.Clear();
            foreach (ScanlineEdge edge in active)
            {
                double start = Math.Max(y, edge.MinimumY), end = Math.Min((double)y + 1, edge.MaximumY);
                if (start >= end) { continue; }
                double first = edge.X + (start - edge.MinimumY) * edge.Slope;
                double last = edge.X + (end - edge.MinimumY) * edge.Slope;
                projected.Add(new(edge, Math.Min(first, last), Math.Max(first, last)));
            }
            projected.Sort(static (a, b) => a.MinimumX.CompareTo(b.MinimumX));
            double right = double.NegativeInfinity;
            int initial = 0;
            foreach (ProjectedScanlineEdge projection in projected)
            {
                if (projection.MinimumX > right && initial == 0 && events.Count == 0 && edges.Count > 0)
                {
                    ends.Add(edges.Count);
                }
                right = Math.Max(right, projection.MaximumX);
                ScanlineEdge edge = projection.Edge;
                if (edge.MinimumY <= y) { initial = Normalize(initial + edge.Winding, rule); }
                else { AddEvent(edge.MinimumY, edge.Winding, rule); }
                if (edge.MaximumY < (double)y + 1) { AddEvent(edge.MaximumY, -edge.Winding, rule); }
                edges.Add(edge);
            }
            if (edges.Count > 0) { ends.Add(edges.Count); }
        }

        private void AddEvent(double y, int delta, FillRule rule)
        {
            events.TryGetValue(y, out int current);
            int value = Normalize(current + delta, rule);
            if (value == 0) { events.Remove(y); }
            else { events[y] = value; }
        }

        private static int Normalize(int winding, FillRule rule) => rule switch
        {
            FillRule.NonZero => winding,
            FillRule.EvenOdd => winding & 1,
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };
    }
}
