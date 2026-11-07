// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal static class Vp8InterPrediction
    {
        private static ReadOnlySpan<short> SixTap =>
        [0,0,128,0,0,0, 0,-6,123,12,-1,0, 2,-11,108,36,-8,1, 0,-9,93,50,-6,0,
         3,-16,77,77,-16,3, 0,-6,50,93,-9,0, 1,-8,36,108,-11,2, 0,-1,12,123,-6,0];

        internal static Vp8MotionVector Chroma(in Vp8Macroblock block, int bx, int by, int version)
        {
            int x, y;
            if (block.Mode == 4)
            {
                int first = by * 8 + bx * 2;
                x = block.Blocks[first].X + block.Blocks[first + 1].X + block.Blocks[first + 4].X + block.Blocks[first + 5].X;
                y = block.Blocks[first].Y + block.Blocks[first + 1].Y + block.Blocks[first + 4].Y + block.Blocks[first + 5].Y;
                x = (x + (x < 0 ? -4 : 4)) / 8; y = (y + (y < 0 ? -4 : 4)) / 8;
            }
            else
            {
                x = (block.Motion.X + (block.Motion.X < 0 ? -1 : 1)) / 2;
                y = (block.Motion.Y + (block.Motion.Y < 0 ? -1 : 1)) / 2;
            }
            if (version == 3) { x &= ~7; y &= ~7; }
            return new(x, y);
        }

        internal static void Predict(ReadOnlySpan<byte> reference, int stride, int height, int x, int y,
            Vp8MotionVector motion, int version, Span<byte> prediction)
        {
            x += motion.X >> 3; y += motion.Y >> 3;
            int fx = motion.X & 7, fy = motion.Y & 7;
            if (fx == 0 && fy == 0)
            {
                for (int row = 0; row < 4; row++)
                {
                    int sourceY = Math.Clamp(y + row, 0, height - 1);
                    if (x >= 0 && x <= stride - 4) { reference.Slice(sourceY * stride + x, 4).CopyTo(prediction.Slice(row * 4, 4)); }
                    else { for (int col = 0; col < 4; col++) { prediction[row * 4 + col] = reference[sourceY * stride + Math.Clamp(x + col, 0, stride - 1)]; } }
                }
                return;
            }
            Span<byte> intermediate = stackalloc byte[36];
            bool bilinear = version != 0;
            int first = fy == 0 ? 0 : bilinear ? 0 : -2, lines = fy == 0 ? 4 : bilinear ? 5 : 9;
            ReadOnlySpan<short> horizontal = SixTap.Slice(fx * 6, 6), vertical = SixTap.Slice(fy * 6, 6);
            for (int line = 0; line < lines; line++)
            {
                int source = Math.Clamp(y + first + line, 0, height - 1) * stride;
                for (int col = 0; col < 4; col++)
                {
                    int sum;
                    if (bilinear)
                    { sum = reference[source + Math.Clamp(x + col, 0, stride - 1)] * (8 - fx) + reference[source + Math.Clamp(x + col + 1, 0, stride - 1)] * fx; sum = (sum + 4) >> 3; }
                    else
                    {
                        sum = 0;
                        for (int tap = 0; tap < 6; tap++) { sum += reference[source + Math.Clamp(x + col + tap - 2, 0, stride - 1)] * horizontal[tap]; }
                        sum = Math.Clamp((sum + 64) >> 7, 0, 255);
                    }
                    intermediate[line * 4 + col] = (byte)sum;
                }
            }
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    int sum;
                    if (fy == 0) { sum = intermediate[row * 4 + col]; }
                    else if (bilinear) { sum = (intermediate[row * 4 + col] * (8 - fy) + intermediate[(row + 1) * 4 + col] * fy + 4) >> 3; }
                    else
                    {
                        sum = 0;
                        for (int tap = 0; tap < 6; tap++) { sum += intermediate[(row + tap) * 4 + col] * vertical[tap]; }
                        sum = Math.Clamp((sum + 64) >> 7, 0, 255);
                    }
                    prediction[row * 4 + col] = (byte)sum;
                }
            }
        }
    }
}
