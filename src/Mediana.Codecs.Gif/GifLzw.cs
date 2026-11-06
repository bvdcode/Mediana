// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Gif
{
    internal static class GifLzw
    {
        internal static void Paint(GifFrame frame, Span<byte> pixels, int canvasWidth, CancellationToken token)
        {
            Span<ushort> prefix = stackalloc ushort[4096];
            Span<byte> suffix = stackalloc byte[4096], stack = stackalloc byte[4096];
            int clear = 1 << frame.MinimumCodeSize, end = clear + 1, available = end + 1;
            for (int i = 0; i < clear; i++) { suffix[i] = (byte)i; }
            GifLzwReader reader = new(frame.Blocks.Span);
            int width = frame.MinimumCodeSize + 1, previous = -1, first = 0, written = 0;
            if (reader.Read(width) != clear) { throw new InvalidDataException("GIF LZW must begin with a clear code."); }
            int row = 0, column = 0, pass = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int code = reader.Read(width);
                if (code == clear) { width = frame.MinimumCodeSize + 1; available = end + 1; previous = -1; continue; }
                if (code == end) { break; }
                int original = code, length = 0;
                if (code == available && previous >= 0) { stack[length++] = (byte)first; code = previous; }
                else if (code >= available) { throw new InvalidDataException("Invalid GIF LZW dictionary reference."); }
                while (code >= clear)
                {
                    if (code <= end || code >= available || length >= stack.Length - 1) { throw new InvalidDataException("Invalid GIF LZW prefix chain."); }
                    stack[length++] = suffix[code]; code = prefix[code];
                }
                first = suffix[code]; stack[length++] = (byte)first;
                while (length > 0)
                {
                    if (written++ >= (long)frame.Width * frame.Height) { throw new InvalidDataException("GIF LZW expands beyond its image."); }
                    int index = stack[--length];
                    if (index >= frame.Palette.Length / 3) { throw new InvalidDataException("GIF palette index is out of range."); }
                    if (index != frame.TransparentIndex)
                    {
                        int offset = ((row + frame.Top) * canvasWidth + column + frame.Left) * 4;
                        frame.Palette.Span.Slice(index * 3, 3).CopyTo(pixels[offset..]); pixels[offset + 3] = 255;
                    }
                    if (++column == frame.Width)
                    {
                        column = 0;
                        if (!frame.Interlaced) { row++; }
                        else
                        {
                            ReadOnlySpan<int> steps = [8, 8, 4, 2], starts = [0, 4, 2, 1];
                            row += steps[pass];
                            while (row >= frame.Height && pass < 3) { row = starts[++pass]; }
                        }
                    }
                }
                if (previous >= 0 && available < 4096)
                {
                    prefix[available] = (ushort)previous; suffix[available++] = (byte)first;
                    if (available == 1 << width && width < 12) { width++; }
                }
                previous = original;
            }
            if (written != (long)frame.Width * frame.Height) { throw new InvalidDataException("GIF LZW image is incomplete."); }
        }
    }
}
