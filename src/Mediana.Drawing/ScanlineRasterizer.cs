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
        internal static void Paint(VectorPath path, Matrix3x2 transform, FillRule rule, int width, int height,
            Action<int, int, int> span, CancellationToken cancellationToken)
        {
            ScanlineEdgeTable edges = new(path, transform, width, height, cancellationToken);
            var crossings = new List<ScanlineCrossing>();
            for (int y = edges.Top; y < edges.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                edges.SelectRow(y);
                CrossingsAt(edges.Active, y + 0.5f, crossings);
                PaintRow(crossings, rule, width, y, span);
            }
        }

        private static void PaintRow(List<ScanlineCrossing> crossings, FillRule rule, int width, int y, Action<int, int, int> span)
        {
            ScanlineSpanEnumerator spans = new(CollectionsMarshal.AsSpan(crossings), rule);
            while (spans.MoveNext(out float start, out float end))
            {
                int left = (int)Math.Clamp(MathF.Ceiling(start - 0.5f), 0, width);
                int right = (int)Math.Clamp(MathF.Ceiling(end - 0.5f), 0, width);
                if (right > left)
                {
                    span(y, left, right);
                }
            }
        }
    }
}
