// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class MicrosoftMpeg4PictureDecoder
    {
        private static ReadOnlySpan<byte> FilterStrength =>
            [0, 1, 1, 2, 2, 3, 3, 4, 4, 4, 5, 5, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 9, 10, 10, 10, 11, 11, 11, 12, 12, 12];

        private void FilterMacroblock(Span<byte> pixels, int mbX, int mbY, ReadOnlySpan<byte> skipped)
        {
            int x = mbX * 16;
            int y = mbY * 16;
            bool current = IsCoded(skipped, mbX, mbY);
            bool bottom = mbY + 1 == mbHeight;
            int strength = FilterStrength[quantizer];
            if (current)
            {
                FilterEdge(pixels, stride, x, y + 8, strength, acrossX: false);
                FilterEdge(pixels, stride, x + 8, y + 8, strength, acrossX: false);
            }
            if (mbY != 0)
            {
                bool above = IsCoded(skipped, mbX, mbY - 1);
                if (current || above)
                {
                    FilterEdge(pixels, stride, x, y, strength, acrossX: false);
                    FilterEdge(pixels, stride, x + 8, y, strength, acrossX: false);
                    FilterChroma(pixels, x / 2, y / 2, strength, acrossX: false);
                }
                if (above)
                {
                    FilterEdge(pixels, stride, x + 8, y - 8, strength, acrossX: true);
                }
                if (mbX != 0 && (above || IsCoded(skipped, mbX - 1, mbY - 1)))
                {
                    FilterEdge(pixels, stride, x, y - 8, strength, acrossX: true);
                    FilterChroma(pixels, x / 2, y / 2 - 8, strength, acrossX: true);
                }
            }
            if (current)
            {
                FilterEdge(pixels, stride, x + 8, y, strength, acrossX: true);
                if (bottom)
                {
                    FilterEdge(pixels, stride, x + 8, y + 8, strength, acrossX: true);
                }
            }
            if (mbX != 0 && (current || IsCoded(skipped, mbX - 1, mbY)))
            {
                FilterEdge(pixels, stride, x, y, strength, acrossX: true);
                if (bottom)
                {
                    FilterEdge(pixels, stride, x, y + 8, strength, acrossX: true);
                    FilterChroma(pixels, x / 2, y / 2, strength, acrossX: true);
                }
            }
        }

        private bool IsCoded(ReadOnlySpan<byte> skipped, int mbX, int mbY) =>
            skipped.IsEmpty || skipped[mbY * mbWidth + mbX] == 0;

        private void FilterChroma(Span<byte> pixels, int x, int y, int strength, bool acrossX)
        {
            FilterEdge(pixels[lumaLength..], stride / 2, x, y, strength, acrossX);
            FilterEdge(pixels[(lumaLength + lumaLength / 4)..], stride / 2, x, y, strength, acrossX);
        }

        private static void FilterEdge(Span<byte> pixels, int stride, int x, int y, int strength, bool acrossX)
        {
            int along = acrossX ? stride : 1;
            int across = acrossX ? 1 : stride;
            int center = y * stride + x;
            for (int i = 0; i < 8; i++, center += along)
            {
                int a = pixels[center - 2 * across];
                int b = pixels[center - across];
                int c = pixels[center];
                int d = pixels[center + across];
                int gradient = (a - d + 4 * (c - b)) / 8;
                int magnitude = Math.Abs(gradient);
                int correction = Math.Min(magnitude, Math.Max(0, 2 * strength - magnitude));
                int inner = Math.Sign(gradient) * correction;
                int outer = Math.Clamp((a - d) / 4, -(correction / 2), correction / 2);
                pixels[center - 2 * across] = (byte)(a - outer);
                pixels[center - across] = (byte)Math.Clamp(b + inner, 0, 255);
                pixels[center] = (byte)Math.Clamp(c - inner, 0, 255);
                pixels[center + across] = (byte)(d + outer);
            }
        }
    }
}
