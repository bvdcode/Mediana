// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    public partial class RasterCanvas
    {
        internal void DrawGlyphCoverage(GlyphCoverage glyph, int left, int top, Rgba32 color, CancellationToken token)
        {
            long originX = (long)left + glyph.Left, originY = (long)top + glyph.Top;
            int first = (int)Math.Clamp(-originX, 0, glyph.Width), last = (int)Math.Clamp(_target.Width - originX, 0, glyph.Width);
            int firstRow = (int)Math.Clamp(-originY, 0, glyph.Height), lastRow = (int)Math.Clamp(_target.Height - originY, 0, glyph.Height);
            if (last <= first || lastRow <= firstRow) { return; }
            for (int y = firstRow; y < lastRow; y++)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<float> row = glyph.Pixels.AsSpan(y * glyph.Width, glyph.Width);
                PaintCoverageRow((int)(y + originY), (int)(first + originX), row.Slice(first, last - first), color, ColorBlendMode.Normal);
            }
        }
    }
}
