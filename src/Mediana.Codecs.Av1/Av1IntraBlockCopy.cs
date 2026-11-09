// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1IntraBlockCopy
    {
        internal static void Apply<T>(Span<T> plane, int stride, int x, int y, int width, int height,
            int vectorX, int vectorY, int subsamplingX, int subsamplingY) where T : unmanaged, IBinaryInteger<T>
        {
            ArgumentOutOfRangeException.ThrowIfNegative(subsamplingX);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(subsamplingX, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(subsamplingY);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(subsamplingY, 1);
            int sourceX = x + (vectorX >> subsamplingX);
            int sourceY = y + (vectorY >> subsamplingY);
            int horizontal = vectorX & ((1 << subsamplingX) - 1);
            int vertical = vectorY & ((1 << subsamplingY) - 1);
            int phase = (vertical << 1) | horizontal;
            if (phase == 0)
            {
                for (int row = 0; row < height; row++)
                {
                    plane.Slice((sourceY + row) * stride + sourceX, width)
                        .CopyTo(plane.Slice((y + row) * stride + x, width));
                }
                return;
            }
            for (int row = 0; row < height; row++)
            {
                int source = (sourceY + row) * stride + sourceX;
                Span<T> destination = plane.Slice((y + row) * stride + x, width);
                for (int column = 0; column < width; column++)
                {
                    int index = source + column;
                    int value = phase switch
                    {
                        1 => (int.CreateChecked(plane[index]) + int.CreateChecked(plane[index + 1]) + 1) >> 1,
                        2 => (int.CreateChecked(plane[index]) + int.CreateChecked(plane[index + stride]) + 1) >> 1,
                        3 => (int.CreateChecked(plane[index]) + int.CreateChecked(plane[index + 1]) +
                            int.CreateChecked(plane[index + stride]) + int.CreateChecked(plane[index + stride + 1]) + 2) >> 2,
                        _ => throw new InvalidDataException("The intra block copy sample phase is invalid.")
                    };
                    destination[column] = T.CreateChecked(value);
                }
            }
        }
    }
}
