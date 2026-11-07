// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal static partial class Vp8ImageDecoder
    {
        private static void ToRgba(ReadOnlySpan<byte> yPlane, ReadOnlySpan<byte> uPlane, ReadOnlySpan<byte> vPlane, int stride, int width, int height, Span<byte> output, CancellationToken token)
        {
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                int y0 = Math.Max(0, (y - 1) / 2), y1 = Math.Min((height - 1) / 2, (y + 1) / 2), wy = (y & 1) == 0 ? 3 : 1;
                if (y == 0) { y0 = y1 = 0; }
                for (int x = 0; x < width; x++)
                {
                    int x0 = Math.Max(0, (x - 1) / 2), x1 = Math.Min((width - 1) / 2, (x + 1) / 2), wx = (x & 1) == 0 ? 3 : 1;
                    if (x == 0) { x0 = x1 = 0; }
                    int u = Chroma(uPlane, stride / 2, x0, x1, y0, y1, wx, wy) - 128;
                    int v = Chroma(vPlane, stride / 2, x0, x1, y0, y1, wx, wy) - 128;
                    int value = yPlane[y * stride + x] - 16, offset = (y * width + x) * 4;
                    output[offset] = Byte((19077 * value + 26149 * v + 8192) >> 14);
                    output[offset + 1] = Byte((19077 * value - 6419 * u - 13320 * v + 8192) >> 14);
                    output[offset + 2] = Byte((19077 * value + 33050 * u + 8192) >> 14); output[offset + 3] = 255;
                }
            }
        }
        private static int Chroma(ReadOnlySpan<byte> data, int stride, int x0, int x1, int y0, int y1, int wx, int wy)
            => (data[y0 * stride + x0] * (4 - wx) * (4 - wy) + data[y0 * stride + x1] * wx * (4 - wy)
                + data[y1 * stride + x0] * (4 - wx) * wy + data[y1 * stride + x1] * wx * wy + 8) >> 4;
    }
}
