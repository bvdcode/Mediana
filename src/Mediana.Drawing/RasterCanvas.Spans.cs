// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    public partial class RasterCanvas
    {
        private void PaintSpan(int y, int left, int right, Rgba32 color, ColorBlendMode mode)
        {
            if (color.A != 255 || mode != ColorBlendMode.Normal)
            {
                for (int x = left; x < right; x++) { Paint(x, y, color, mode); }
                return;
            }
            if (_clip is null) { FillOpaqueSpan(y, left, right, color); return; }
            int row = y * _target.Width;
            int position = left;
            while (position < right)
            {
                while (position < right && _clip[row + position] == 0) { position++; }
                int start = position;
                while (position < right && _clip[row + position] != 0) { position++; }
                if (position > start) { FillOpaqueSpan(y, start, position, color); }
            }
        }

        private void FillOpaqueSpan(int y, int left, int right, Rgba32 color)
        {
            int start = y * _target.Width + left;
            int length = right - left;
            PixelCompositor.Fill(_target.Pixels.Span.Slice(start * 4, length * 4), color);
            if (_coverage is not null) { _coverage.AsSpan(start, length).Fill(255); }
            if (_shape is not null)
            {
                _shape.AsSpan(start, length).Fill(255);
                _paintLeft = Math.Min(_paintLeft, left);
                _paintTop = Math.Min(_paintTop, y);
                _paintRight = Math.Max(_paintRight, right);
                _paintBottom = Math.Max(_paintBottom, y + 1);
            }
        }
    }
}
