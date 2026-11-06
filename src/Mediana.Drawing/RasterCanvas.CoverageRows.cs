// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    public partial class RasterCanvas
    {
        private void PaintCoverageRow(int y, int left, ReadOnlySpan<float> coverage, Rgba32 color, ColorBlendMode mode)
        {
            if (coverage.Length >= System.Numerics.Vector<float>.Count && mode == ColorBlendMode.Normal
                && _clip is null && _coverage is null && _shape is null)
            {
                PixelCompositor.BlendCoverageSpan(_target.Pixels.Span.Slice((y * _target.Width + left) * 4, coverage.Length * 4), coverage, color);
                return;
            }
            for (int x = 0; x < coverage.Length; x++)
            {
                float value = Math.Clamp(coverage[x], 0, 1);
                if (value > 254.5f / 255)
                {
                    int start = x;
                    while (x + 1 < coverage.Length && coverage[x + 1] > 254.5f / 255) { x++; }
                    PaintSpan(y, left + start, left + x + 1, color, mode);
                }
                else if (value > 0)
                {
                    Paint(x + left, y, color with { A = (byte)MathF.Round(color.A * value) }, mode, (byte)MathF.Round(255 * value));
                }
            }
        }
    }
}
