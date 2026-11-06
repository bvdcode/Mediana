// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    public partial class RasterCanvas
    {
        /// <summary>Fills a transformed path with fractional pixel coverage at the canvas resolution.</summary>
        public void FillPathAntialiased(VectorPath path, Rgba32 color, Matrix3x2 transform, FillRule rule = FillRule.NonZero,
            ColorBlendMode blendMode = ColorBlendMode.Normal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ScanlineEdgeTable edges = new(path, transform, _target.Width, _target.Height, cancellationToken);
            FillCoverage(edges, color, rule, blendMode, cancellationToken);
        }

        internal void FillPathAntialiased(FlattenedPath path, Rgba32 color, Vector2 offset, CancellationToken token)
        {
            ScanlineEdgeTable edges = new(path.Contours, offset, _target.Width, _target.Height);
            FillCoverage(edges, color, FillRule.NonZero, ColorBlendMode.Normal, token);
        }

        private void FillCoverage(ScanlineEdgeTable edges, Rgba32 color, FillRule rule, ColorBlendMode blendMode, CancellationToken cancellationToken)
        {
            ScanlineRasterizer.PaintCoverage(edges, rule,
                (x, y, coverage) => Paint(x, y, color with { A = (byte)MathF.Round(color.A * coverage) }, blendMode,
                    (byte)MathF.Round(255 * coverage)), cancellationToken,
                (y, left, right) => PaintSpan(y, left, right, color, blendMode));
        }
    }
}
