// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal static partial class ScanlineRasterizer
    {
        internal static void PaintCoverage(VectorPath path, Matrix3x2 transform, FillRule rule, int width, int height,
            Action<int, int, float> pixel, CancellationToken cancellationToken, Action<int, int, int>? span = null)
        {
            ScanlineEdgeTable edges = new(path, transform, width, height, cancellationToken);
            PaintCoverage(edges, rule, pixel, cancellationToken, span);
        }

        internal static void PaintCoverage(ScanlineEdgeTable edges, FillRule rule, Action<int, int, float> pixel,
            CancellationToken cancellationToken, Action<int, int, int>? span = null)
        {
            if (edges.Bottom <= edges.Top || edges.Right <= edges.Left) { return; }
            int coverageWidth = edges.Right - edges.Left;
            Span<float> coverage = coverageWidth <= 256 ? stackalloc float[coverageWidth] : new float[coverageWidth];
            List<double> divisions = [];
            List<ScanlineCrossing> crossings = [];
            for (int y = edges.Top; y < edges.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                coverage.Clear();
                edges.SelectRow(y);
                DivideRow(edges.Active, y, divisions);
                for (int i = 1; i < divisions.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double start = divisions[i - 1], end = divisions[i];
                    if (end <= start) { continue; }
                    double sample = (start + end) / 2;
                    CrossingsAt(edges.Active, sample, crossings);
                    double split = FindCrossing(CollectionsMarshal.AsSpan(crossings), sample, start, end);
                    if (split > start && split < end)
                    {
                        if (divisions.Count >= 1_000_000) { throw new InvalidOperationException("A raster row exceeds one million edge intersections."); }
                        divisions.Insert(i, split); i--; continue;
                    }
                    ScanlineSpanEnumerator spans = new(CollectionsMarshal.AsSpan(crossings), rule);
                    while (spans.MoveNextEdges(out ScanlineCrossing left, out ScanlineCrossing right))
                    {
                        ScanlineCoverageIntegrator.Add(coverage, edges.Left,
                            left.X + (start - sample) * left.Slope, left.X + (end - sample) * left.Slope,
                            right.X + (start - sample) * right.Slope, right.X + (end - sample) * right.Slope, (float)(end - start));
                    }
                }
                for (int x = 0; x < coverage.Length; x++)
                {
                    if (span is not null && coverage[x] > 254.5f / 255)
                    {
                        int start = x;
                        while (x + 1 < coverage.Length && coverage[x + 1] > 254.5f / 255) { x++; }
                        span(y, start + edges.Left, x + edges.Left + 1);
                    }
                    else if (coverage[x] > 0) { pixel(x + edges.Left, y, Math.Min(1, coverage[x])); }
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
        }

        private static double FindCrossing(ReadOnlySpan<ScanlineCrossing> crossings, double sample, double start, double end)
        {
            double first = end, tolerance = Math.Abs(Math.BitIncrement(sample) - sample) * 8;
            for (int i = 1; i < crossings.Length; i++)
            {
                ScanlineCrossing a = crossings[i - 1], b = crossings[i];
                if (a.Slope == b.Slope) { continue; }
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
            crossings.Sort(static (a, b) => a.X.CompareTo(b.X));
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
