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
            ScanlineRasterizer.PaintCoverage(path, transform, rule, _target.Width, _target.Height,
                (x, y, coverage) => Paint(x, y, color with { A = (byte)MathF.Round(color.A * coverage) }, blendMode,
                    (byte)MathF.Round(255 * coverage)), cancellationToken);
        }
    }
}
