// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9IntraPrediction
    {
        internal static void Predict<T>(Span<T> plane, int stride, int maxX, int maxY, int x, int y, int size, int mode,
            bool haveLeft, bool haveAbove, bool haveRight, int depth) where T : unmanaged, IBinaryInteger<T>
        {
            Span<int> top = stackalloc int[65], left = stackalloc int[32]; int middle = 1 << (depth - 1);
            top[0] = haveAbove ? haveLeft ? int.CreateTruncating(plane[(y - 1) * stride + x - 1]) : middle + 1 : middle - 1;
            for (int i = 0; i < size; i++)
            {
                top[i + 1] = haveAbove ? int.CreateTruncating(plane[(y - 1) * stride + Math.Min(maxX, x + i)]) : middle - 1;
                left[i] = haveLeft ? int.CreateTruncating(plane[Math.Min(maxY, y + i) * stride + x - 1]) : middle + 1;
            }
            for (int i = size; i < 2 * size; i++)
            { top[i + 1] = haveAbove && haveRight && size == 4 ? int.CreateTruncating(plane[(y - 1) * stride + Math.Min(maxX, x + i)]) : top[size]; }
            if (mode == 0)
            {
                int sum = 0, count = 0;
                if (haveAbove) { for (int i = 0; i < size; i++) { sum += top[i + 1]; } count += size; }
                if (haveLeft) { for (int i = 0; i < size; i++) { sum += left[i]; } count += size; }
                T dc = T.CreateTruncating(count == 0 ? middle : (sum + count / 2) / count);
                for (int row = 0; row < size; row++) { plane.Slice((y + row) * stride + x, size).Fill(dc); }
                return;
            }
            for (int row = 0; row < size; row++)
            {
                Span<T> destination = plane.Slice((y + row) * stride + x, size);
                for (int column = 0; column < size; column++)
                {
                    int value = mode switch
                    {
                        1 => top[column + 1],
                        2 => left[row],
                        3 => row + column + 2 < size * 2 ? Average3(top[row + column + 1], top[row + column + 2], top[row + column + 3]) : top[size * 2],
                        4 => Diagonal135(top, left, row - column),
                        5 => Diagonal117(top, left, row, column),
                        6 => Diagonal153(top, left, row, column),
                        7 => (column & 1) == 0 ? Average2(left[Math.Min(size - 1, row + column / 2)], left[Math.Min(size - 1, row + column / 2 + 1)]) :
                            Average3(left[Math.Min(size - 1, row + column / 2)], left[Math.Min(size - 1, row + column / 2 + 1)], left[Math.Min(size - 1, row + column / 2 + 2)]),
                        8 => (row & 1) == 0 ? Average2(top[row / 2 + column + 1], top[row / 2 + column + 2]) :
                            Average3(top[row / 2 + column + 1], top[row / 2 + column + 2], top[row / 2 + column + 3]),
                        9 => Math.Clamp(top[column + 1] + left[row] - top[0], 0, (1 << depth) - 1),
                        _ => throw new InvalidDataException("Invalid VP9 intra prediction mode.")
                    };
                    destination[column] = T.CreateTruncating(value);
                }
            }
        }

        private static int Average2(int a, int b) => (a + b + 1) >> 1;
        private static int Average3(int a, int b, int c) => (a + 2 * b + c + 2) >> 2;
        private static int Border(ReadOnlySpan<int> top, ReadOnlySpan<int> left, int index) => index >= 0 ? top[index] : left[-index - 1];
        private static int Diagonal135(ReadOnlySpan<int> top, ReadOnlySpan<int> left, int diagonal) =>
            Average3(Border(top, left, -diagonal - 1), Border(top, left, -diagonal), Border(top, left, -diagonal + 1));

        private static int Diagonal117(ReadOnlySpan<int> top, ReadOnlySpan<int> left, int row, int column)
        {
            int steps = Math.Min(row / 2, column); row -= 2 * steps; column -= steps;
            if (row == 0) { return Average2(top[column], top[column + 1]); }
            if (row == 1 && column > 0) { return Average3(top[column - 1], top[column], top[column + 1]); }
            return Average3(Border(top, left, 2 - row), Border(top, left, 1 - row), Border(top, left, -row));
        }

        private static int Diagonal153(ReadOnlySpan<int> top, ReadOnlySpan<int> left, int row, int column)
        {
            int steps = Math.Min(row, column / 2); row -= steps; column -= steps * 2;
            if (column == 0) { return row == 0 ? Average2(left[0], top[0]) : Average2(left[row - 1], left[row]); }
            if (column == 1) { return Average3(Border(top, left, 1 - row), Border(top, left, -row), Border(top, left, -row - 1)); }
            return Average3(top[column - 2], top[column - 1], top[column]);
        }
    }
}
