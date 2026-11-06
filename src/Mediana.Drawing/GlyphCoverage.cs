// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    internal class GlyphCoverage
    {
        internal float[] Pixels { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal int Left { get; }
        internal int Top { get; }

        internal GlyphCoverage(FlattenedPath path, Vector2 fraction, CancellationToken token)
        {
            Left = checked((int)MathF.Floor(path.Left + fraction.X));
            Top = checked((int)MathF.Floor(path.Top + fraction.Y));
            Width = Math.Max(0, checked((int)MathF.Ceiling(path.Right + fraction.X)) - Left);
            Height = Math.Max(0, checked((int)MathF.Ceiling(path.Bottom + fraction.Y)) - Top);
            Pixels = new float[checked(Width * Height)];
            if (Pixels.Length == 0) { return; }
            ScanlineEdgeTable edges = new(path, fraction - new Vector2(Left, Top), Width, Height);
            ScanlineRasterizer.PaintCoverageRows(edges, FillRule.NonZero,
                (y, left, values) => values.CopyTo(Pixels.AsSpan(y * Width + left, values.Length)), token);
        }
    }
}
