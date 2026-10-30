// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Numerics;
using System.Threading;

namespace Mediana.Images.Svg
{
    /// <summary>Renders an interpreted SVG viewport with the managed CPU raster canvas.</summary>
    public static class SvgRenderer
    {
        /// <summary>Renders an SVG to RGBA pixels. Unsupported features require explicit partial-render opt-in.</summary>
        public static PixelBuffer Render(SvgDocument document, DrawingRenderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new DrawingRenderOptions();
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.AllowPartialRendering && document.UnsupportedFeatures.Count != 0)
            {
                throw new NotSupportedException("Unsupported SVG features: " + string.Join("; ", document.UnsupportedFeatures));
            }

            return SceneRasterizer.Render(document.Root, document.Width, document.Height, options,
                (width, height) => Viewport(document.ViewBox, width, height, document.AspectRatio), cancellationToken);
        }

        internal static Matrix3x2 Viewport((float X, float Y, float Width, float Height) box, float width, float height, string aspect)
        {
            float sx = width / box.Width, sy = height / box.Height;
            string[] parts = aspect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string align = parts.Length == 0 ? "xMidYMid" : parts[0];
            if (align == "defer")
            {
                align = parts.Length > 1 ? parts[1] : "xMidYMid";
            }

            if (align == "none")
            {
                return Matrix3x2.CreateTranslation(-box.X, -box.Y) * Matrix3x2.CreateScale(sx, sy);
            }

            float scale = aspect.Contains("slice", StringComparison.Ordinal) ? Math.Max(sx, sy) : Math.Min(sx, sy);
            float x = align.Contains("xMax", StringComparison.Ordinal) ? 1 : align.Contains("xMin", StringComparison.Ordinal) ? 0 : 0.5f;
            float y = align.Contains("YMax", StringComparison.Ordinal) ? 1 : align.Contains("YMin", StringComparison.Ordinal) ? 0 : 0.5f;
            return Matrix3x2.CreateTranslation(-box.X, -box.Y) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((width - (box.Width * scale)) * x, (height - (box.Height * scale)) * y);
        }
    }
}
