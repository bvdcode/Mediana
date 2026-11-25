// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg4Visual
{
    internal static class Wmv2InverseDct
    {
        internal static void Write(Span<int> coefficients, Span<byte> target, int stride, bool intra)
        {
            for (int y = 0; y < 8; y++)
            {
                Transform(coefficients[(y * 8)..], 1, false);
            }
            for (int x = 0; x < 8; x++)
            {
                Transform(coefficients[x..], 8, true);
            }
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int index = y * stride + x;
                    int value = coefficients[y * 8 + x];
                    if (!intra)
                    {
                        value += target[index];
                    }
                    target[index] = (byte)Math.Clamp(value, 0, 255);
                }
            }
        }

        private static void Transform(Span<int> values, int step, bool column)
        {
            int inputShift = column ? 3 : 0;
            int inputRound = column ? 4 : 0;
            int outputShift = column ? 14 : 8;
            int outputRound = 1 << (outputShift - 1);

            int evenSum = ((values[0] + values[4 * step]) * 2048) >> inputShift;
            int evenDifference = ((values[0] - values[4 * step]) * 2048) >> inputShift;
            int evenFirst = (values[2 * step] * 2676 + values[6 * step] * 1108 + inputRound) >> inputShift;
            int evenSecond = (values[2 * step] * 1108 - values[6 * step] * 2676 + inputRound) >> inputShift;
            int oddFirst = (values[step] * 2841 + values[7 * step] * 565 + inputRound) >> inputShift;
            int oddFourth = (values[step] * 565 - values[7 * step] * 2841 + inputRound) >> inputShift;
            int oddSecond = (values[3 * step] * 2408 + values[5 * step] * 1609 + inputRound) >> inputShift;
            int oddThird = (values[5 * step] * 2408 - values[3 * step] * 1609 + inputRound) >> inputShift;

            int middleSum = oddFirst - oddSecond;
            int middleDifference = oddFourth - oddThird;
            StorePair(values, step, 0, evenSum + evenFirst, oddFirst + oddSecond, outputRound, outputShift);
            StorePair(values, step, 1, evenDifference + evenSecond, (181 * (middleSum + middleDifference) + 128) >> 8, outputRound, outputShift);
            StorePair(values, step, 2, evenDifference - evenSecond, (181 * (middleSum - middleDifference) + 128) >> 8, outputRound, outputShift);
            StorePair(values, step, 3, evenSum - evenFirst, oddFourth + oddThird, outputRound, outputShift);
        }

        private static void StorePair(Span<int> values, int step, int index, int even, int odd, int rounding, int shift)
        {
            values[index * step] = unchecked((short)((even + odd + rounding) >> shift));
            values[(7 - index) * step] = unchecked((short)((even - odd + rounding) >> shift));
        }
    }
}
