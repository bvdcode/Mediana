// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg4Visual
{
    internal static class Wmv2AdaptiveDct
    {
        internal static void Add(Span<int> coefficients, Span<byte> target, int stride, bool wide)
        {
            int width = wide ? 8 : 4;
            int height = wide ? 4 : 8;
            for (int y = 0; y < height; y++)
            {
                Span<int> values = coefficients[(y * 8)..];
                if (wide)
                {
                    TransformEight(values, 1, column: false);
                }
                else
                {
                    TransformFour(values, 1, column: false);
                }
            }
            for (int x = 0; x < width; x++)
            {
                Span<int> values = coefficients[x..];
                if (wide)
                {
                    TransformFour(values, 8, column: true);
                }
                else
                {
                    TransformEight(values, 8, column: true);
                }
                for (int y = 0; y < height; y++)
                {
                    int index = y * stride + x;
                    target[index] = (byte)Math.Clamp(target[index] + values[y * 8], 0, 255);
                }
            }
        }

        private static void TransformFour(Span<int> values, int step, bool column)
        {
            int shift = column ? 17 : 11;
            int unit = column ? 2896 : 23170;
            int first = column ? 3784 : 30274;
            int second = column ? 1567 : 12540;
            int rounding = 1 << (shift - 1);
            int sum = unit * (values[0] + values[2 * step]) + rounding;
            int difference = unit * (values[0] - values[2 * step]) + rounding;
            int outer = first * values[step] + second * values[3 * step];
            int inner = second * values[step] - first * values[3 * step];
            StorePair(values, step, 0, 3, sum, outer, shift);
            StorePair(values, step, 1, 2, difference, inner, shift);
        }

        private static void TransformEight(Span<int> values, int step, bool column)
        {
            int shift = column ? 20 : 11;
            if (!column && values[1..8].IndexOfAnyExcept(0) < 0)
            {
                values[..8].Fill(unchecked((short)(values[0] * 8)));
                return;
            }
            const int w1 = 22725, w2 = 21407, w3 = 19266, w4 = 16383, w5 = 12873, w6 = 8867, w7 = 4520;
            int rounding = column ? w4 * ((1 << (shift - 1)) / w4) : 1 << (shift - 1);
            int sum = (values[0] + values[4 * step]) * w4 + rounding;
            int difference = (values[0] - values[4 * step]) * w4 + rounding;
            int even1 = values[2 * step] * w2 + values[6 * step] * w6;
            int even2 = values[2 * step] * w6 - values[6 * step] * w2;
            int a = values[step], b = values[3 * step], c = values[5 * step], d = values[7 * step];
            int odd0 = a * w1 + b * w3 + c * w5 + d * w7;
            int odd1 = a * w3 - b * w7 - c * w1 - d * w5;
            int odd2 = a * w5 - b * w1 + c * w7 + d * w3;
            int odd3 = a * w7 - b * w5 + c * w3 - d * w1;
            StorePair(values, step, 0, 7, sum + even1, odd0, shift);
            StorePair(values, step, 1, 6, difference + even2, odd1, shift);
            StorePair(values, step, 2, 5, difference - even2, odd2, shift);
            StorePair(values, step, 3, 4, sum - even1, odd3, shift);
        }

        private static void StorePair(Span<int> values, int step, int first, int second, int even, int odd, int shift)
        {
            values[first * step] = unchecked((short)((even + odd) >> shift));
            values[second * step] = unchecked((short)((even - odd) >> shift));
        }
    }
}
