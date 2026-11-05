// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    internal static class Vp8PixelSource
    {
        internal static bool ReadMacroblock(VideoFrame source, int x, int y, Span<byte> planes)
        {
            bool alpha = false;
            for (int row = 0; row < 16; row += 2)
            {
                ReadOnlySpan<byte> upper = source.GetRow(0, Math.Min(y + row, source.Format.Height - 1));
                ReadOnlySpan<byte> lower = source.GetRow(0, Math.Min(y + row + 1, source.Format.Height - 1));
                for (int column = 0; column < 16; column += 2)
                {
                    int first = Math.Min(x + column, source.Format.Width - 1) * 4;
                    int second = Math.Min(x + column + 1, source.Format.Width - 1) * 4;
                    alpha |= (upper[first + 3] & upper[second + 3] & lower[first + 3] & lower[second + 3]) != 255;
                    planes[row * 16 + column] = Luma(upper, first);
                    planes[row * 16 + column + 1] = Luma(upper, second);
                    planes[(row + 1) * 16 + column] = Luma(lower, first);
                    planes[(row + 1) * 16 + column + 1] = Luma(lower, second);
                    int red = upper[first] + upper[second] + lower[first] + lower[second];
                    int green = upper[first + 1] + upper[second + 1] + lower[first + 1] + lower[second + 1];
                    int blue = upper[first + 2] + upper[second + 2] + lower[first + 2] + lower[second + 2];
                    int chroma = (row / 2) * 8 + column / 2;
                    planes[256 + chroma] = (byte)Math.Clamp(128 + ((-38 * red - 74 * green + 112 * blue + 512) >> 10), 0, 255);
                    planes[320 + chroma] = (byte)Math.Clamp(128 + ((112 * red - 94 * green - 18 * blue + 512) >> 10), 0, 255);
                }
            }
            return alpha;
        }

        private static byte Luma(ReadOnlySpan<byte> row, int index) =>
            (byte)(16 + ((66 * row[index] + 129 * row[index + 1] + 25 * row[index + 2] + 128) >> 8));
    }
}
