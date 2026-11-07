// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class Mpeg4PictureDecoder
    {
        private static int MotionDifference(ref Mpeg4VisualBitReader bits, int code)
        {
            int magnitude = Mpeg4Codebooks.Motion.Read(ref bits);
            if (magnitude == 0) { return 0; }
            bool negative = bits.Flag(); int residual = bits.Read(code - 1);
            int value = ((magnitude - 1) << (code - 1)) + residual + 1;
            return negative ? -value : value;
        }
        private static int WrapMotion(int value, int code)
        {
            int limit = 32 << (code - 1);
            if (value < -limit) { return value + limit * 2; }
            if (value >= limit) { return value - limit * 2; } return value;
        }
        private void ReadMotion(ref Mpeg4VisualBitReader bits, int mb, bool four, int code, int bound)
        {
            Span<int> state = Motion;
            for (int block = 0; block < (four ? 4 : 1); block++)
            {
                int leftMb = mb % mbWidth > 0 ? mb - 1 : -1, topMb = mb - mbWidth;
                int rightMb = mb % mbWidth + 1 < mbWidth ? mb - mbWidth + 1 : -1;
                int leftBlock = 1, topBlock = 2, rightBlock = 2;
                switch (block)
                {
                    case 0: break;
                    case 1: leftMb = mb; leftBlock = 0; topBlock = 3; break;
                    case 2: topMb = rightMb = mb; leftBlock = 3; topBlock = 0; rightBlock = 1; break;
                    case 3: leftMb = topMb = rightMb = mb; leftBlock = 2; topBlock = 0; rightBlock = 1; break;
                    default: throw new InvalidOperationException("Invalid MPEG-4 Visual motion block.");
                }
                if (leftMb < bound) { leftMb = -1; } if (topMb < bound) { topMb = -1; } if (rightMb < bound) { rightMb = -1; }
                for (int axis = 0; axis < 2; axis++)
                {
                    int a = leftMb < 0 ? 0 : state[leftMb * 10 + leftBlock * 2 + axis];
                    int b = topMb < 0 ? 0 : state[topMb * 10 + topBlock * 2 + axis];
                    int c = rightMb < 0 ? 0 : state[rightMb * 10 + rightBlock * 2 + axis];
                    int count = (leftMb >= 0 ? 1 : 0) + (topMb >= 0 ? 1 : 0) + (rightMb >= 0 ? 1 : 0);
                    int predictor = count <= 1 ? a + b + c : a + b + c - Math.Min(a, Math.Min(b, c)) - Math.Max(a, Math.Max(b, c));
                    state[mb * 10 + block * 2 + axis] = WrapMotion(predictor + MotionDifference(ref bits, code), code);
                }
            }
            if (!four) { for (int i = 1; i < 4; i++) { state[mb * 10 + i * 2] = state[mb * 10]; state[mb * 10 + i * 2 + 1] = state[mb * 10 + 1]; } }
        }
        private void Predict(Span<byte> target, ReadOnlySpan<byte> reference, int mb, ReadOnlySpan<int> motion, bool four, bool rounding, bool average)
        {
            int x = mb % mbWidth * 16, y = mb / mbWidth * 16;
            for (int block = 0; block < (four ? 4 : 1); block++)
            {
                int bx = x + (block & 1) * 8, by = y + (block >> 1) * 8, size = four ? 8 : 16;
                if (config.QuarterPel) { PredictQuarter(target, reference, Width, Height, bx, by, size, motion[block * 2], motion[block * 2 + 1], rounding, average); }
                else { PredictHalf(target, reference, Width, Height, bx, by, size, size, motion[block * 2], motion[block * 2 + 1], rounding, average); }
            }
            for (int plane = 1; plane < 3; plane++)
            {
                int mx, my;
                if (four)
                {
                    ReadOnlySpan<byte> round = [0,0,0,1,1,1,1,1,0,0,0,0,0,0,1,1];
                    int sx = 0, sy = 0;
                    for (int i = 0; i < 4; i++) { sx += config.QuarterPel ? motion[i * 2] / 2 : motion[i * 2]; sy += config.QuarterPel ? motion[i * 2 + 1] / 2 : motion[i * 2 + 1]; }
                    mx = (sx >> 3) + round[sx & 15]; my = (sy >> 3) + round[sy & 15];
                }
                else
                {
                    int vx = config.QuarterPel ? motion[0] / 2 : motion[0], vy = config.QuarterPel ? motion[1] / 2 : motion[1];
                    mx = (vx >> 1) | (vx & 1); my = (vy >> 1) | (vy & 1);
                }
                int offset = lumaSize + (plane - 1) * (lumaSize / 4);
                PredictHalf(target[offset..], reference[offset..], Width / 2, Height / 2, x / 2, y / 2, 8, 8, mx, my, rounding, average);
            }
        }
        private static void PredictHalf(Span<byte> target, ReadOnlySpan<byte> reference, int width, int height,
            int x, int y, int blockWidth, int blockHeight, int mx, int my, bool rounding, bool average)
        {
            int dx = mx >> 1, dy = my >> 1, fx = mx & 1, fy = my & 1, round = rounding ? 0 : 1;
            bool interior = x + dx >= 0 && y + dy >= 0 && x + dx + blockWidth + fx <= width && y + dy + blockHeight + fy <= height;
            if (interior && fx == 0 && fy == 0 && !average)
            {
                for (int row = 0; row < blockHeight; row++) { reference.Slice((y + dy + row) * width + x + dx, blockWidth).CopyTo(target.Slice((y + row) * width + x, blockWidth)); }
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
    }
}
