// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1RestorationSelfGuided
    {
        internal static void Filter<T>(Av1RestorationSource<T> source, Span<T> output, int stride, int x, int y, int width, int height, Av1RestorationUnit unit, int bitDepth = 8) where T : unmanaged, IBinaryInteger<T>
        {
            int coefficientShift = bitDepth switch
            {
                8 => 0,
                10 => 2,
                12 => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(bitDepth))
            };
            int maximum = (1 << bitDepth) - 1;
            Span<int> first = stackalloc int[width * height];
            Span<int> second = stackalloc int[width * height];
            bool useFirst = Av1SelfGuidedParameters.Radius(unit.Set, 0) != 0;
            bool useSecond = Av1SelfGuidedParameters.Radius(unit.Set, 1) != 0;
            if (useFirst) { Box(source, first, x, y, width, height, unit.Set, 0, coefficientShift); }
            if (useSecond) { Box(source, second, x, y, width, height, unit.Set, 1, coefficientShift); }
            int thirdWeight = 128 - unit.X0 - unit.X1;
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int original = source.Center(x + column, y + row) << 4;
                    int index = row * width + column;
                    int sum = unit.X1 * original + unit.X0 * (useFirst ? first[index] : original) + thirdWeight * (useSecond ? second[index] : original);
                    output[(y + row) * stride + x + column] = T.CreateTruncating(Math.Clamp((sum + 1024) >> 11, 0, maximum));
                }
            }
        }

        private static void Box<T>(Av1RestorationSource<T> source, Span<int> filtered, int x, int y, int width, int height, int set, int pass, int coefficientShift) where T : unmanaged, IBinaryInteger<T>
        {
            int sumBias = coefficientShift == 0 ? 0 : 1 << (coefficientShift - 1);
            int squaresBias = coefficientShift == 0 ? 0 : 1 << (2 * coefficientShift - 1);
            int radius = Av1SelfGuidedParameters.Radius(set, pass);
            int epsilon = Av1SelfGuidedParameters.Epsilon(set, pass);
            int count = (2 * radius + 1) * (2 * radius + 1);
            int denominator = count * count * epsilon;
            int scale = ((1 << 20) + denominator / 2) / denominator;
            int reciprocal = ((1 << 12) + count / 2) / count;
            int columns = width + 2;
            Span<int> a = stackalloc int[(height + 2) * columns];
            Span<int> b = stackalloc int[(height + 2) * columns];
            int sumColumns = width + 2 + 2 * radius;
            Span<int> columnSums = stackalloc int[sumColumns];
            Span<int> columnSquares = stackalloc int[sumColumns];
            for (int column = 0; column < sumColumns; column++)
            {
                int sum = 0;
                int squares = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int sample = source.Sample(x + column - radius - 1, y - 1 + dy);
                    sum += sample;
                    squares += sample * sample;
                }
                columnSums[column] = sum;
                columnSquares[column] = squares;
            }
            for (int row = -1; row <= height; row++)
            {
                if (row >= 0)
                {
                    for (int column = 0; column < sumColumns; column++)
                    {
                        int previous = source.Sample(x + column - radius - 1, y + row - radius - 1);
                        int next = source.Sample(x + column - radius - 1, y + row + radius);
                        columnSums[column] += next - previous;
                        columnSquares[column] += next * next - previous * previous;
                    }
                }
                if (pass == 0 && (row & 1) == 0) { continue; }
                int sum = 0;
                int squares = 0;
                for (int column = 0; column <= 2 * radius; column++)
                {
                    sum += columnSums[column];
                    squares += columnSquares[column];
                }
                for (int column = -1; column <= width; column++)
                {
                    int normalizedSquares = (squares + squaresBias) >> (2 * coefficientShift);
                    int normalizedSum = (sum + sumBias) >> coefficientShift;
                    int variance = Math.Max(0, normalizedSquares * count - normalizedSum * normalizedSum);
                    int z = (int)(((long)variance * scale + (1 << 19)) >> 20);
                    int attenuation = z >= 255 ? 256 : z == 0 ? 1 : ((z << 8) + z / 2) / (z + 1);
                    int index = (row + 1) * columns + column + 1;
                    a[index] = attenuation;
                    b[index] = checked((int)(((256 - attenuation) * (long)sum * reciprocal + 2048) >> 12));
                    if (column < width)
                    {
                        sum += columnSums[column + 2 * radius + 2] - columnSums[column + 1];
                        squares += columnSquares[column + 2 * radius + 2] - columnSquares[column + 1];
                    }
                }
            }
            for (int row = 0; row < height; row++)
            {
                int shift = pass == 0 && (row & 1) != 0 ? 4 : 5;
                for (int column = 0; column < width; column++)
                {
                    int sumA = 0;
                    int sumB = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (pass == 0 && ((row + dy) & 1) == 0) { continue; }
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int weight = pass == 0 ? dx == 0 ? 6 : 5 : dx == 0 || dy == 0 ? 4 : 3;
                            int index = (row + dy + 1) * columns + column + dx + 1;
                            sumA += weight * a[index];
                            sumB += weight * b[index];
                        }
                    }
                    int value = sumA * source.Center(x + column, y + row) + sumB;
                    filtered[row * width + column] = (value + (1 << (8 + shift - 5))) >> (8 + shift - 4);
                }
            }
        }
    }
}
