// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    public partial class RasterCanvas
    {
        private bool TryDrawTranslatedImage(ReadOnlySpan<byte> source, int width, int height, int stride,
            Matrix3x2 transform, float opacity, ColorBlendMode mode, CancellationToken token)
        {
            if (_coverage is not null || _shape is not null || transform.M11 != 1 || transform.M12 != 0 ||
                transform.M21 != 0 || transform.M22 != 1 || transform.M31 < int.MinValue || transform.M31 >= int.MaxValue ||
                transform.M32 < int.MinValue || transform.M32 >= int.MaxValue ||
                transform.M31 != MathF.Truncate(transform.M31) || transform.M32 != MathF.Truncate(transform.M32))
            { return false; }
            int dx = (int)transform.M31, dy = (int)transform.M32;
            long left = Math.Max(0, (long)dx), top = Math.Max(0, (long)dy),
                right = Math.Min(_target.Width, (long)dx + width), bottom = Math.Min(_target.Height, (long)dy + height);
            if (right <= left || bottom <= top) { return true; }
            Span<byte> destination = _target.Pixels.Span;
            for (int y = (int)top; y < bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                int start = (y * _target.Width + (int)left) * 4;
                int offset = (y - dy) * stride + ((int)left - dx) * 4;
                int end = start + ((int)right - (int)left) * 4;
                for (int i = start; i < end; i += 4, offset += 4)
                {
                    if (source[offset + 3] == 0 || (_clip is not null && _clip[i / 4] == 0)) { continue; }
                    Rgba32 color = new(source[offset], source[offset + 1], source[offset + 2],
                        (byte)MathF.Round(source[offset + 3] * opacity));
                    PixelCompositor.Blend(destination, i, color, mode);
                }
            }
            return true;
        }
    }
}
