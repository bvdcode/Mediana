// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg4Visual
{
    internal static class Wmv2MotionCompensation
    {
        internal static void Predict(Span<byte> target, ReadOnlySpan<byte> reference, int stride, int paddedHeight,
            int width, int height, int x, int y, int motionX, int motionY, bool quarterStep)
        {
            int startX = Math.Clamp(x + (motionX >> 1), -16, width);
            int startY = Math.Clamp(y + (motionY >> 1), -16, height);
            int phaseX = startX <= -16 || startX >= width ? 0 : ((motionX & 1) << 1) + (quarterStep ? 1 : 0);
            bool halfY = startY > -16 && startY < height && (motionY & 1) != 0;
            Span<byte> horizontal = stackalloc byte[19 * 16];
            if (phaseX != 0)
            {
                int firstRow = halfY ? -1 : 0;
                int endRow = halfY ? 18 : 16;
                for (int row = firstRow; row < endRow; row++)
                {
                    int sourceRow = Math.Clamp(startY + row, 0, paddedHeight - 1);
                    ReadOnlySpan<byte> source = reference.Slice(sourceRow * stride, stride);
                    for (int column = 0; column < 16; column++)
                    {
                        int at = startX + column;
                        horizontal[(row - firstRow) * 16 + column] = Filter(
                            source[Math.Clamp(at - 1, 0, stride - 1)], source[Math.Clamp(at, 0, stride - 1)],
                            source[Math.Clamp(at + 1, 0, stride - 1)], source[Math.Clamp(at + 2, 0, stride - 1)]);
                    }
                }
            }
            for (int row = 0; row < 16; row++)
            {
                Span<byte> output = target.Slice((y + row) * stride + x, 16);
                for (int column = 0; column < 16; column++)
                {
                    if (phaseX != 0)
                    {
                        int at = row * 16 + column;
                        byte half = halfY
                            ? Filter(horizontal[at], horizontal[at + 16], horizontal[at + 32], horizontal[at + 48])
                            : horizontal[at];
                        if (phaseX == 2)
                        {
                            output[column] = half;
                            continue;
                        }
                        int columnBias = phaseX == 3 ? 1 : 0;
                        byte integer = ReadColumn(reference, stride, paddedHeight, startX + column + columnBias, startY + row, halfY);
                        output[column] = (byte)((integer + half + 1) >> 1);
                    }
                    else
                    {
                        output[column] = ReadColumn(reference, stride, paddedHeight, startX + column, startY + row, halfY);
                    }
                }
            }
        }

        private static byte ReadColumn(ReadOnlySpan<byte> reference, int stride, int height, int x, int y, bool half)
        {
            x = Math.Clamp(x, 0, stride - 1);
            byte center = reference[Math.Clamp(y, 0, height - 1) * stride + x];
            if (!half)
            {
                return center;
            }
            return Filter(reference[Math.Clamp(y - 1, 0, height - 1) * stride + x], center,
                reference[Math.Clamp(y + 1, 0, height - 1) * stride + x], reference[Math.Clamp(y + 2, 0, height - 1) * stride + x]);
        }

        private static byte Filter(int a, int b, int c, int d) =>
            (byte)Math.Clamp((9 * (b + c) - a - d + 8) >> 4, 0, 255);
    }
}
