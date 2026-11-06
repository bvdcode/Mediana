// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    public partial class TextRenderer
    {
        private void DrawPreparedGlyph(RasterCanvas canvas, FlattenedPath path, Rgba32 color, Vector2 offset, CancellationToken token)
        {
            const int subpixelResolution = 256;
            Vector2 snapped = new((float)(Math.Round((double)offset.X * subpixelResolution) / subpixelResolution),
                (float)(Math.Round((double)offset.Y * subpixelResolution) / subpixelResolution));
            if (!float.IsFinite(snapped.X) || !float.IsFinite(snapped.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Glyph placement must be finite.");
            }
            Vector2 integral = new(MathF.Floor(snapped.X), MathF.Floor(snapped.Y));
            Vector2 fraction = snapped - integral;
            (FlattenedPath Path, Vector2 Fraction) key = (path, fraction);
            if ((double)integral.X < int.MinValue || (double)integral.X > int.MaxValue || (double)integral.Y < int.MinValue || (double)integral.Y > int.MaxValue)
            {
                canvas.FillPathAntialiased(path, color, offset, token);
                return;
            }
            if (!_glyphCoverage.TryGetValue(key, out GlyphCoverage? coverage))
            {
                double width = Math.Ceiling(path.Right + fraction.X) - Math.Floor(path.Left + fraction.X);
                double height = Math.Ceiling(path.Bottom + fraction.Y) - Math.Floor(path.Top + fraction.Y);
                if (width <= 0 || height <= 0) { return; }
                if (width * height > _options.MaximumPathCommands || Math.Abs(path.Left) > int.MaxValue / 2
                    || Math.Abs(path.Top) > int.MaxValue / 2 || width > int.MaxValue || height > int.MaxValue)
                {
                    canvas.FillPathAntialiased(path, color, offset, token);
                    return;
                }
                coverage = new(path, fraction, token);
                if (coverage.Pixels.Length > _options.MaximumPathCommands - _coveragePixels)
                {
                    _glyphCoverage.Clear(); _coveragePixels = 0;
                }
                _glyphCoverage.Add(key, coverage); _coveragePixels += coverage.Pixels.Length;
            }
            canvas.DrawGlyphCoverage(coverage, (int)integral.X, (int)integral.Y, color, token);
        }
    }
}
