// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1CdefDirection
    {
        internal static int Find(ReadOnlySpan<byte> pixels, int stride, out int variance)
        {
            Span<int> partial = stackalloc int[8 * 15];
            Span<int> cost = stackalloc int[8];
            partial.Clear();
            cost.Clear();
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int sample = pixels[y * stride + x] - 128;
                    partial[y + x] += sample;
                    partial[15 + y + x / 2] += sample;
                    partial[30 + y] += sample;
                    partial[45 + 3 + y - x / 2] += sample;
                    partial[60 + 7 + y - x] += sample;
                    partial[75 + 3 - y / 2 + x] += sample;
                    partial[90 + x] += sample;
                    partial[105 + y / 2 + x] += sample;
                }
            }
            ReadOnlySpan<int> division = [0, 840, 420, 280, 210, 168, 140, 120, 105];
            for (int index = 0; index < 8; index++)
            {
                cost[2] += Square(partial[30 + index]);
                cost[6] += Square(partial[90 + index]);
            }
            cost[2] *= division[8];
            cost[6] *= division[8];
            for (int index = 0; index < 7; index++)
            {
                cost[0] += (Square(partial[index]) + Square(partial[14 - index])) * division[index + 1];
                cost[4] += (Square(partial[60 + index]) + Square(partial[74 - index])) * division[index + 1];
            }
            cost[0] += Square(partial[7]) * division[8];
            cost[4] += Square(partial[67]) * division[8];
            for (int direction = 1; direction < 8; direction += 2)
            {
                ReadOnlySpan<int> row = partial.Slice(direction * 15, 15);
                for (int index = 0; index < 5; index++) { cost[direction] += Square(row[3 + index]); }
                cost[direction] *= division[8];
                for (int index = 0; index < 3; index++)
                {
                    cost[direction] += (Square(row[index]) + Square(row[10 - index])) * division[2 * index + 2];
                }
            }
            int best = 0;
            for (int direction = 1; direction < 8; direction++)
            {
                if (cost[direction] > cost[best]) { best = direction; }
            }
            variance = (cost[best] - cost[(best + 4) & 7]) >> 10;
            return best;
        }

        private static int Square(int value) => value * value;
    }
}
