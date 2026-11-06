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
                for (int x = first; x < last; x++)
                {
                    float coverage = row[x];
                    if (coverage == 1)
                    {
                        int start = x;
                        while (x + 1 < last && row[x + 1] == 1) { x++; }
                        PaintSpan((int)(y + originY), (int)(start + originX), (int)(x + originX + 1), color, ColorBlendMode.Normal);
                    }
                    else if (coverage > 0)
                    {
                        Paint((int)(x + originX), (int)(y + originY), color with { A = (byte)MathF.Round(color.A * coverage) }, ColorBlendMode.Normal,
                            (byte)MathF.Round(255 * coverage));
                    }
                }
            }
        }
    }
}
