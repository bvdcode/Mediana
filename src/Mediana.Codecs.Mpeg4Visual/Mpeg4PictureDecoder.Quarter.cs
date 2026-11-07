// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class Mpeg4PictureDecoder
    {
        private static void PredictQuarter(Span<byte> target, ReadOnlySpan<byte> reference, int width, int height,
            int x, int y, int size, int mx, int my, bool rounding, bool average)
        {
            int fx = mx & 3, fy = my & 3, dx = mx >> 2, dy = my >> 2, round = rounding ? 0 : 1;
            if (fx == 0 && fy == 0)
            { PredictHalf(target, reference, width, height, x, y, size, size, dx * 2, dy * 2, rounding, average); return; }
            Span<byte> horizontal = stackalloc byte[17 * 16];
            int rows = fy == 0 ? size : size + 1;
            ReadOnlySpan<int> filter = [-1,3,-6,20,20,-6,3,-1];
            for (int row = 0; row < rows; row++)
            {
                int ry = Math.Clamp(y + dy + row, 0, height - 1) * width;
                for (int column = 0; column < size; column++)
                {
                    int value;
                    if (fx == 0) { value = reference[ry + Math.Clamp(x + dx + column, 0, width - 1)]; }
                    else
                    {
                        int sum = 0;
                        for (int tap = 0; tap < 8; tap++)
                        {
                            int local = Mirror(column + tap - 3, size + 1);
                            sum += reference[ry + Math.Clamp(x + dx + local, 0, width - 1)] * filter[tap];
                        }
                        value = Math.Clamp((sum + 15 + round) >> 5, 0, 255);
                        if ((fx & 1) != 0)
                        { value = (value + reference[ry + Math.Clamp(x + dx + column + (fx >> 1), 0, width - 1)] + round) >> 1; }
                    }
                    horizontal[row * size + column] = (byte)value;
                }
            }
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int value;
                    if (fy == 0) { value = horizontal[row * size + column]; }
                    else
                    {
                        int sum = 0;
                        for (int tap = 0; tap < 8; tap++) { sum += horizontal[Mirror(row + tap - 3, size + 1) * size + column] * filter[tap]; }
                        value = Math.Clamp((sum + 15 + round) >> 5, 0, 255);
                        if ((fy & 1) != 0) { value = (value + horizontal[(row + (fy >> 1)) * size + column] + round) >> 1; }
                    }
                    int at = (y + row) * width + x + column;
                    target[at] = (byte)(average ? (target[at] + value + 1) >> 1 : value);
                }
            }
        }
        private static int Mirror(int index, int length)
        {
            if (index < 0) { return -index - 1; }
            if (index >= length) { return 2 * length - index - 1; } return index;
        }
    }
}
