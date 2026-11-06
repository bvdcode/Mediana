// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mediana.Drawing
{
    internal static partial class ScanlineRasterizer
    {
        private const int CoverageSamples = 8;

        internal static void PaintCoverage(VectorPath path, Matrix3x2 transform, FillRule rule, int width, int height,
            Action<int, int, float> pixel, CancellationToken cancellationToken)
        {
            ScanlineEdgeTable edges = new(path, transform, width, height, cancellationToken);
            if (edges.Bottom <= edges.Top || edges.Right <= edges.Left) { return; }
            int coverageWidth = edges.Right - edges.Left;
            Span<float> coverage = coverageWidth <= 256 ? stackalloc float[coverageWidth] : new float[coverageWidth];
            List<float> divisions = new List<float>();
            List<ScanlineCrossing> crossings = new List<ScanlineCrossing>();
            for (int y = edges.Top; y < edges.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                coverage.Clear();
                edges.SelectRow(y);
                DivideRow(edges.Active, y, divisions);
                for (int i = 1; i < divisions.Count; i++)
                {
                    float start = divisions[i - 1];
                    float end = divisions[i];
                    if (end > start)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CrossingsAt(edges.Active, (start + end) / 2, crossings);
                        ScanlineSpanEnumerator spans = new(CollectionsMarshal.AsSpan(crossings), rule);
                        while (spans.MoveNext(out float left, out float right))
                        {
                            AddCoverage(coverage, edges.Left, left, right, end - start);
                        }
                    }
                }

                for (int x = 0; x < coverage.Length; x++)
                {
                    if (coverage[x] > 0)
                    {
                        pixel(x + edges.Left, y, Math.Min(1, coverage[x]));
                    }
                }
            }
        }

        private static void DivideRow(ReadOnlySpan<ScanlineEdge> edges, int y, List<float> divisions)
        {
            divisions.Clear();
            for (int i = 0; i <= CoverageSamples; i++)
            {
                divisions.Add(y + (i / (float)CoverageSamples));
            }

            foreach (ScanlineEdge edge in edges)
            {
                if (edge.MinimumY > y && edge.MinimumY < y + 1)
                {
                    divisions.Add(edge.MinimumY);
                }

                if (edge.MaximumY > y && edge.MaximumY < y + 1)
                {
                    divisions.Add(edge.MaximumY);
                }
            }

            divisions.Sort();
        }

        private static void CrossingsAt(ReadOnlySpan<ScanlineEdge> edges, float sample, List<ScanlineCrossing> crossings)
        {
            crossings.Clear();
            foreach (ScanlineEdge edge in edges)
            {
                if (sample >= edge.MinimumY && sample < edge.MaximumY)
                {
                    crossings.Add(new ScanlineCrossing(edge.X + ((sample - edge.MinimumY) * edge.Slope), edge.Winding));
                }
            }

            crossings.Sort((a, b) => a.X.CompareTo(b.X));
        }

        private static void AddCoverage(Span<float> coverage, int origin, float start, float end, float height)
        {
            float left = Math.Clamp(start, origin, origin + coverage.Length);
            float right = Math.Clamp(end, origin, origin + coverage.Length);
            for (int x = (int)MathF.Floor(left); x < MathF.Ceiling(right); x++)
            {
                coverage[x - origin] += (Math.Min(right, x + 1) - Math.Max(left, x)) * height;
            }
        }
    }
}
