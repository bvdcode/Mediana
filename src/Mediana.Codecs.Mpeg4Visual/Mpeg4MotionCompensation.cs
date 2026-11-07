// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal static class Mpeg4MotionCompensation
    {
        internal static void PredictHalf(Span<byte> target, ReadOnlySpan<byte> reference, int width, int height,
            int x, int y, int blockWidth, int blockHeight, int mx, int my, bool rounding, bool average)
        {
            int dx = mx >> 1, dy = my >> 1, fx = mx & 1, fy = my & 1, round = rounding ? 0 : 1;
            bool interior = x + dx >= 0 && y + dy >= 0 && x + dx + blockWidth + fx <= width && y + dy + blockHeight + fy <= height;
            if (interior)
            {
                for (int row = 0; row < blockHeight; row++)
                {
                    ReadOnlySpan<byte> top = reference.Slice((y + dy + row) * width + x + dx, blockWidth + fx);
                    ReadOnlySpan<byte> bottom = reference.Slice((y + dy + row + fy) * width + x + dx, blockWidth + fx);
                    Span<byte> output = target.Slice((y + row) * width + x, blockWidth);
                    if (fx == 0 && fy == 0 && !average) { top.CopyTo(output); continue; }
                    int column = 0;
                    if (Vector128.IsHardwareAccelerated)
                    {
                        for (; column + 8 <= blockWidth; column += 8)
                        {
                            Vector128<ushort> value = Load8(top, column);
                            if (fx != 0) { value += Load8(top, column + 1); }
                            if (fy != 0)
                            {
                                value += Load8(bottom, column);
                                if (fx != 0) { value += Load8(bottom, column + 1); }
                            }
                            if (fx + fy == 1) { value = (value + Vector128.Create((ushort)round)) >> 1; }
                            else if (fx + fy == 2) { value = (value + Vector128.Create((ushort)(round + 1))) >> 2; }
                            if (average) { value = (value + Load8(output, column) + Vector128.Create((ushort)1)) >> 1; }
                            Vector128.Narrow(value, Vector128<ushort>.Zero).GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)column);
                        }
                    }
                    for (; column < blockWidth; column++)
                    {
                        int value = top[column];
                        if (fx != 0) { value += top[column + 1]; }
                        if (fy != 0) { value += bottom[column]; if (fx != 0) { value += bottom[column + 1]; } }
                        if (fx + fy == 1) { value = (value + round) >> 1; }
                        else if (fx + fy == 2) { value = (value + round + 1) >> 2; }
                        output[column] = (byte)(average ? (output[column] + value + 1) >> 1 : value);
                    }
                }
                return;
            }
            for (int row = 0; row < blockHeight; row++)
            {
                int ry = Math.Clamp(y + dy + row, 0, height - 1), ry1 = Math.Clamp(y + dy + row + fy, 0, height - 1);
                for (int column = 0; column < blockWidth; column++)
                {
                    int rx = Math.Clamp(x + dx + column, 0, width - 1), rx1 = Math.Clamp(x + dx + column + fx, 0, width - 1);
                    int value;
                    if (fx == 0 && fy == 0) { value = reference[ry * width + rx]; }
                    else if (fy == 0) { value = (reference[ry * width + rx] + reference[ry * width + rx1] + round) >> 1; }
                    else if (fx == 0) { value = (reference[ry * width + rx] + reference[ry1 * width + rx] + round) >> 1; }
                    else { value = (reference[ry * width + rx] + reference[ry * width + rx1] + reference[ry1 * width + rx] + reference[ry1 * width + rx1] + round + 1) >> 2; }
                    int at = (y + row) * width + x + column;
                    target[at] = (byte)(average ? (target[at] + value + 1) >> 1 : value);
                }
            }
        }

        private static Vector128<ushort> Load8(ReadOnlySpan<byte> row, int column) =>
            Vector128.WidenLower(Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(row), (nuint)column).ToVector128());
    }
}
