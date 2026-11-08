// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1Transform4
    {
        internal static void Inverse(Span<int> values, int type)
        {
            (int horizontal, int vertical) = type switch
            {
                0 => (0, 0),
                1 => (0, 1),
                2 => (1, 0),
                3 => (1, 1),
                9 => (2, 2),
                10 => (2, 0),
                11 => (0, 2),
                _ => throw new NotSupportedException("The AV1 four-point transform type is not implemented.")
            };
            for (int row = 0; row < 4; row++)
            {
                Transform(values.Slice(row * 4, 4), horizontal);
            }
            Span<int> column = stackalloc int[4];
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    column[y] = Math.Clamp(values[y * 4 + x], short.MinValue, short.MaxValue);
                }
                Transform(column, vertical);
                for (int y = 0; y < 4; y++)
                {
                    values[y * 4 + x] = (column[y] + 8) >> 4;
                }
            }
        }

        private static void Transform(Span<int> values, int type)
        {
            switch (type)
            {
                case 0:
                    (values[1], values[2]) = (values[2], values[1]);
                    int first = Round((long)(values[0] + values[1]) * 2896);
                    int second = Round((long)(values[0] - values[1]) * 2896);
                    int third = Round((long)values[2] * 1567 - (long)values[3] * 3784);
                    int fourth = Round((long)values[2] * 3784 + (long)values[3] * 1567);
                    Validate(first); Validate(second); Validate(third); Validate(fourth);
                    values[0] = Math.Clamp(first + fourth, short.MinValue, short.MaxValue);
                    values[3] = Math.Clamp(first - fourth, short.MinValue, short.MaxValue);
                    values[1] = Math.Clamp(second + third, short.MinValue, short.MaxValue);
                    values[2] = Math.Clamp(second - third, short.MinValue, short.MaxValue);
                    break;
                case 1:
                    long a = (long)values[0] * 1321 + (long)values[2] * 3803 + (long)values[3] * 2482;
                    long b = (long)values[0] * 2482 - (long)values[2] * 1321 - (long)values[3] * 3803;
                    long c = (long)values[1] * 3344;
                    int combined = values[0] - values[2] + values[3];
                    Validate(combined);
                    values[0] = Round(a + c);
                    values[1] = Round(b + c);
                    values[2] = Round((long)combined * 3344);
                    values[3] = Round(a + b - c);
                    for (int index = 0; index < 4; index++) { Validate(values[index]); }
                    break;
                case 2:
                    for (int index = 0; index < 4; index++)
                    {
                        values[index] = Round((long)values[index] * 5793);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static int Round(long value) => checked((int)((value + 2048) >> 12));

        private static void Validate(int value)
        {
            if (value is < short.MinValue or > short.MaxValue)
            {
                throw new InvalidDataException("The AV1 transform intermediate exceeds its signed precision.");
            }
        }
    }
}
