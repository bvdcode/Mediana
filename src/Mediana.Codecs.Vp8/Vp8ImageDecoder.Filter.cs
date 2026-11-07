// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal static partial class Vp8ImageDecoder
    {
        private static void Filter(Span<byte> y, Span<byte> u, Span<byte> v, int stride, int columns, int rows, ReadOnlySpan<byte> filters, Vp8FrameHeader header, CancellationToken token)
        {
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    token.ThrowIfCancellationRequested();
                    int offset = (row * columns + column) * 2, level = filters[offset];
                    if (level == 0) { continue; }
                    int inner = level;
                    if (header.Sharpness != 0) { inner = Math.Min(inner >> (header.Sharpness > 4 ? 2 : 1), 9 - header.Sharpness); }
                    inner = Math.Max(1, inner); int hev = level >= 40 ? 2 : level >= 15 ? 1 : 0;
                    for (int plane = 0; plane < (header.SimpleFilter ? 1 : 3); plane++)
                    {
                        Span<byte> data = plane switch { 0 => y, 1 => u, 2 => v, _ => throw new InvalidDataException("Invalid VP8 plane.") };
                        int size = plane == 0 ? 16 : 8, pitch = plane == 0 ? stride : stride / 2, start = row * size * pitch + column * size;
                        if (column > 0) { Edge(data, start, 1, pitch, size, 2 * (level + 2) + inner, inner, hev, true, header.SimpleFilter); }
                        if (filters[offset + 1] != 0)
                        {
                            for (int x = 4; x < size; x += 4) { Edge(data, start + x, 1, pitch, size, 2 * level + inner, inner, hev, false, header.SimpleFilter); }
                        }
                        if (row > 0) { Edge(data, start, pitch, 1, size, 2 * (level + 2) + inner, inner, hev, true, header.SimpleFilter); }
                        if (filters[offset + 1] != 0)
                        {
                            for (int line = 4; line < size; line += 4) { Edge(data, start + line * pitch, pitch, 1, size, 2 * level + inner, inner, hev, false, header.SimpleFilter); }
                        }
                    }
                }
            }
        }
        private static void Edge(Span<byte> pixels, int start, int step, int along, int count, int limit, int inner, int hev, bool macroblock, bool simple)
        {
            for (int index = 0; index < count; index++)
            {
                int at = start + index * along;
                int p0 = pixels[at - step], p1 = pixels[at - 2 * step], q0 = pixels[at], q1 = pixels[at + step];
                if (2 * Math.Abs(p0 - q0) + (Math.Abs(p1 - q1) >> 1) > limit) { continue; }
                bool high = Math.Abs(p1 - p0) > hev || Math.Abs(q1 - q0) > hev;
                if (!simple)
                {
                    if (Math.Abs(pixels[at - 4 * step] - pixels[at - 3 * step]) > inner || Math.Abs(pixels[at - 3 * step] - p1) > inner
                        || Math.Abs(p1 - p0) > inner || Math.Abs(pixels[at + 3 * step] - pixels[at + 2 * step]) > inner
                        || Math.Abs(pixels[at + 2 * step] - q1) > inner || Math.Abs(q1 - q0) > inner) { continue; }
                }
                bool outer = simple || high;
                int value = Clamp((outer || macroblock ? Clamp(p1 - q1) : 0) + 3 * (q0 - p0));
                if (!simple && macroblock && !high)
                {
                    for (int distance = 0; distance < 3; distance++)
                    {
                        int change = ((27 - distance * 9) * value + 63) >> 7;
                        pixels[at - (distance + 1) * step] = Byte(pixels[at - (distance + 1) * step] + change);
                        pixels[at + distance * step] = Byte(pixels[at + distance * step] - change);
                    }
                }
                else
                {
                    int first = Clamp(value + 4) >> 3, second = Clamp(value + 3) >> 3;
                    pixels[at - step] = Byte(p0 + second); pixels[at] = Byte(q0 - first);
                    if (!outer)
                    {
                        int change = (first + 1) >> 1;
                        pixels[at - 2 * step] = Byte(p1 + change); pixels[at + step] = Byte(q1 - change);
                    }
                }
            }
        }
        private static int Clamp(int value) => Math.Clamp(value, -128, 127);
        private static byte Byte(int value) => (byte)Math.Clamp(value, 0, 255);
    }
}
