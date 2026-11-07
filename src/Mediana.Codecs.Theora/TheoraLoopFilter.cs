// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static class TheoraLoopFilter
    {
        internal static void Apply(TheoraLayout layout, TheoraFrameState state, Span<byte> output, int limit, CancellationToken token)
        {
            if (limit == 0) { return; }
            for (int plane = 0; plane < 3; plane++)
            {
                int width = layout.PlaneWidth(plane), height = layout.PlaneHeight(plane);
                Span<byte> pixels = output.Slice(layout.PlaneOffset(plane), width * height);
                for (int by = 0; by < height / 8; by++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int bx = 0; bx < width / 8; bx++)
                    {
                        if (state.Coded[layout.Block(plane, bx, by)] == 0) { continue; }
                        int x = bx * 8, y = by * 8;
                        if (bx > 0) { Horizontal(pixels, width, height, x - 2, y, limit); }
                        if (by > 0) { Vertical(pixels, width, height, x, y - 2, limit); }
                        if (x + 8 < width && state.Coded[layout.Block(plane, bx + 1, by)] == 0)
                        { Horizontal(pixels, width, height, x + 6, y, limit); }
                        if (y + 8 < height && state.Coded[layout.Block(plane, bx, by + 1)] == 0)
                        { Vertical(pixels, width, height, x, y + 6, limit); }
                    }
                }
            }
        }

        private static void Horizontal(Span<byte> pixels, int stride, int height, int x, int y, int limit)
        {
            for (int i = 0; i < 8; i++) { Filter(pixels, (height - 1 - y - i) * stride + x, 1, limit); }
        }
        private static void Vertical(Span<byte> pixels, int stride, int height, int x, int y, int limit)
        {
            for (int i = 0; i < 8; i++) { Filter(pixels, (height - 1 - y) * stride + x + i, -stride, limit); }
        }
        private static void Filter(Span<byte> pixels, int start, int step, int limit)
        {
            int response = (pixels[start] - 3 * pixels[start + step] + 3 * pixels[start + 2 * step] - pixels[start + 3 * step] + 4) >> 3;
            int magnitude = Math.Abs(response);
            int value = magnitude < limit ? magnitude : Math.Max(0, 2 * limit - magnitude);
            value *= Math.Sign(response);
            pixels[start + step] = (byte)Math.Clamp(pixels[start + step] + value, 0, 255);
            pixels[start + step * 2] = (byte)Math.Clamp(pixels[start + step * 2] - value, 0, 255);
        }
    }
}
