// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal static partial class ScanlineRasterizer
    {
        private const int PartitionWidth = 128;
        private const int PartitionEdgeCount = 32;
        private const int PartitionDivisionCount = 8;

        internal static void PaintCoverage(VectorPath path, Matrix3x2 transform, FillRule rule, int width, int height,
            Action<int, int, float> pixel, CancellationToken cancellationToken, Action<int, int, int>? span = null)
        {
            ScanlineEdgeTable edges = new(path, transform, width, height, cancellationToken);
            PaintCoverage(edges, rule, pixel, cancellationToken, span);
        }

        internal static void PaintCoverage(ScanlineEdgeTable edges, FillRule rule, Action<int, int, float> pixel,
            CancellationToken cancellationToken, Action<int, int, int>? span = null)
            => PaintCoverageCore(edges, rule, pixel, cancellationToken, span, null);

        internal static void PaintCoverageRows(ScanlineEdgeTable edges, FillRule rule, CoverageRow row, CancellationToken token)
            => PaintCoverageCore(edges, rule, null, token, null, row);

        internal static void PaintCoverageRuns(ScanlineEdgeTable edges, FillRule rule, CoverageRow row,
            Action<int, int, int> span, CancellationToken token)
            => PaintCoverageCore(edges, rule, null, token, span, row);

        private static void PaintCoverageCore(ScanlineEdgeTable edges, FillRule rule, Action<int, int, float>? pixel,
            CancellationToken cancellationToken, Action<int, int, int>? span, CoverageRow? row)
        {
            if (edges.Bottom <= edges.Top || edges.Right <= edges.Left) { return; }
            int coverageWidth = edges.Right - edges.Left;
            Span<float> coverage = coverageWidth <= 256 ? stackalloc float[coverageWidth] : new float[coverageWidth];
            List<double> divisions = [];
            List<ScanlineCrossing> crossings = [];
            ScanlineRowPartitioner? partitions = null;
            for (int y = edges.Top; y < edges.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                edges.SelectRow(y);
                if (edges.Active.IsEmpty) { continue; }
                int left = edges.Left, right = edges.Right;
                if (coverageWidth > 256 && !edges.GetRowBounds(y, out left, out right)) { continue; }
                Span<float> values = coverage.Slice(left - edges.Left, right - left);
                values.Clear();
                DivideRow(edges.Active, y, divisions);
                if (values.Length < PartitionWidth || edges.Active.Length < PartitionEdgeCount || divisions.Count <= PartitionDivisionCount)
                {
                    IntegrateRow(edges.Active, y, values, left, rule, divisions, crossings, cancellationToken);
                }
                else
                {
                    partitions ??= new();
                    partitions.SelectRow(edges.Active, y, rule);
                    int start = 0;
                    foreach (int end in partitions.Ends)
                    {
                        ReadOnlySpan<ScanlineEdge> group = partitions.Edges.Slice(start, end - start);
                        DivideRow(group, y, divisions);
                        IntegrateRow(group, y, values, left, rule, divisions, crossings, cancellationToken);
                        start = end;
                    }
                }
                if (row is not null && span is null && pixel is null) { row(y, left, values); continue; }
                for (int x = 0; x < values.Length; x++)
                {
                    if (span is not null && values[x] > 254.5f / 255)
                    {
                        int start = x;
                        while (x + 1 < values.Length && values[x + 1] > 254.5f / 255) { x++; }
                        span(y, start + left, x + left + 1);
                    }
                    else if (values[x] > 0)
                    {
                        if (row is null) { pixel!(x + left, y, Math.Min(1, values[x])); }
                        else
                        {
                            int start = x;
                            while (x + 1 < values.Length && values[x + 1] > 0 && values[x + 1] <= 254.5f / 255) { x++; }
                            row(y, start + left, values.Slice(start, x - start + 1));
                        }
                    }
                }
            }
        }

        private static void IntegrateRow(ReadOnlySpan<ScanlineEdge> edges, int y, Span<float> coverage, int origin,
            FillRule rule, List<double> divisions, List<ScanlineCrossing> crossings, CancellationToken cancellationToken)
        {
            for (int i = 1; i < divisions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double start = divisions[i - 1], end = divisions[i];
                if (end <= start) { continue; }
                double sample = (start + end) / 2;
                CrossingsAt(edges, sample, crossings);
                double split = FindCrossing(CollectionsMarshal.AsSpan(crossings), sample, start, end);
                if (split > start && split < end)
                {
                    divisions.Insert(i, split); i--; continue;
                }
                ScanlineSpanEnumerator spans = new(CollectionsMarshal.AsSpan(crossings), rule);
                while (spans.MoveNextEdges(out ScanlineCrossing left, out ScanlineCrossing right))
                {
                    ScanlineCoverageIntegrator.Add(coverage, origin,
                        left.X + (start - sample) * left.Slope, left.X + (end - sample) * left.Slope,
                        right.X + (start - sample) * right.Slope, right.X + (end - sample) * right.Slope, (float)(end - start));
                }
            }
        }

        private static void DivideRow(ReadOnlySpan<ScanlineEdge> edges, int y, List<double> divisions)
        {
            divisions.Clear(); divisions.Add(y); divisions.Add(y + 1);
            foreach (ScanlineEdge edge in edges)
            {
                if (edge.MinimumY > y && edge.MinimumY < y + 1) { divisions.Add(edge.MinimumY); }
                if (edge.MaximumY > y && edge.MaximumY < y + 1) { divisions.Add(edge.MaximumY); }
            }
            divisions.Sort();
            int retained = 1;
            for (int i = 1; i < divisions.Count; i++)
            {
                if (divisions[i] != divisions[retained - 1]) { divisions[retained++] = divisions[i]; }
            }
            divisions.RemoveRange(retained, divisions.Count - retained);
        }

        private static double FindCrossing(ReadOnlySpan<ScanlineCrossing> crossings, double sample, double start, double end)
        {
            double first = end, tolerance = Math.Abs(Math.BitIncrement(sample) - sample) * 8;
            for (int i = 1; i < crossings.Length; i++)
            {
                ScanlineCrossing a = crossings[i - 1], b = crossings[i];
                if (a.Slope == b.Slope || b.X - a.X >= Math.Abs(a.Slope - b.Slope) * ((end - start) / 2)) { continue; }
                if (a.Order > b.Order) { (a, b) = (b, a); }
                double intersection = a.OriginY + ((double)b.OriginX - a.OriginX + ((double)a.OriginY - b.OriginY) * b.Slope) / (a.Slope - b.Slope);
                if (intersection > start + tolerance && intersection < end - tolerance) { first = Math.Min(first, intersection); }
            }
            return first;
        }

        private static void CrossingsAt(ReadOnlySpan<ScanlineEdge> edges, double sample, List<ScanlineCrossing> crossings)
        {
            crossings.Clear();
            foreach (ScanlineEdge edge in edges)
            {
                if (sample >= edge.MinimumY && sample < edge.MaximumY)
                { crossings.Add(new(edge.X + (sample - edge.MinimumY) * edge.Slope, edge.Winding) { Slope = edge.Slope, OriginX = edge.X, OriginY = edge.MinimumY, Order = edge.Order }); }
            }
            if (crossings.Count > 16) { crossings.Sort(static (a, b) => a.X.CompareTo(b.X)); }
            else
            {
                Span<ScanlineCrossing> values = CollectionsMarshal.AsSpan(crossings);
                for (int i = 1; i < values.Length; i++)
                {
                    ScanlineCrossing value = values[i];
                    int position = i;
                    while (position > 0 && values[position - 1].X > value.X)
                    {
                        values[position] = values[position - 1]; position--;
                    }
                    values[position] = value;
                }
            }
        }

        private static void CrossingsAt(ReadOnlySpan<ScanlineEdge> edges, float sample, List<ScanlineCrossing> crossings)
        {
            crossings.Clear();
            foreach (ScanlineEdge edge in edges)
            {
                if (sample >= edge.MinimumY && sample < edge.MaximumY)
                { crossings.Add(new(edge.X + (sample - edge.MinimumY) * edge.Slope, edge.Winding)); }
            }
            crossings.Sort(static (a, b) => a.X.CompareTo(b.X));
        }
    }
}
