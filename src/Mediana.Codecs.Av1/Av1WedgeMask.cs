// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1WedgeMask
    {
        private const int MasterSize = 64;

        internal static bool IsSupported(int blockSize) => blockSize is >= 3 and <= 9 or 18 or 19;

        internal static void Initialize(Span<byte> output, int width, int height, int index, int sign)
        {
            if ((width, height) is not ((8, 8) or (8, 16) or (16, 8) or (16, 16) or (16, 32) or (32, 16) or (32, 32) or (8, 32) or (32, 8)))
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The AV1 block size does not support a wedge mask.");
            }
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 15);
            ArgumentOutOfRangeException.ThrowIfNegative(sign);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(sign, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, width * height);
            int shape = height > width ? 0 : height < width ? 1 : 2;
            ReadOnlySpan<byte> entry = Codebook.Slice((shape * 16 + index) * 3, 3);
            int direction = entry[0];
            int x = MasterSize / 2 - ((entry[1] * width) >> 3);
            int y = MasterSize / 2 - ((entry[2] * height) >> 3);
            int sum = 0;
            for (int column = 0; column < width; column++) { sum += Master(direction, y, x + column); }
            for (int row = 1; row < height; row++) { sum += Master(direction, y + row, x); }
            int edge = width + height - 1;
            int flip = (sum + edge / 2) / edge < 32 ? 1 : 0;
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int value = Master(direction, y + row, x + column);
                    output[row * width + column] = (byte)(sign == flip ? value : 64 - value);
                }
            }
        }

        private static int Master(int direction, int row, int column) => direction switch
        {
            0 => Vertical[row],
            1 => Vertical[column],
            2 => Oblique(column, row),
            3 => Oblique(row, column),
            4 => 64 - Oblique(row, MasterSize - 1 - column),
            5 => 64 - Oblique(column, MasterSize - 1 - row),
            _ => throw new InvalidDataException("The AV1 wedge direction is invalid.")
        };

        private static int Oblique(int row, int column)
        {
            int position = column - MasterSize / 4 + (row >> 1) + (row & 1);
            return (row & 1) == 0 ? Even[Math.Clamp(position, 0, MasterSize - 1)] : Odd[Math.Clamp(position, 0, MasterSize - 1)];
        }

        private static ReadOnlySpan<byte> Odd =>
        [
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 6, 18,
            37, 53, 60, 63, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
            64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
        ];

        private static ReadOnlySpan<byte> Even =>
        [
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 4, 11, 27,
            46, 58, 62, 63, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
            64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
        ];

        private static ReadOnlySpan<byte> Vertical =>
        [
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 7, 21,
            43, 57, 62, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
            64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
        ];

        private static ReadOnlySpan<byte> Codebook =>
        [
            2,4,4, 3,4,4, 4,4,4, 5,4,4, 0,4,2, 0,4,4, 0,4,6, 1,4,4,
            2,4,2, 2,4,6, 5,4,2, 5,4,6, 3,2,4, 3,6,4, 4,2,4, 4,6,4,
            2,4,4, 3,4,4, 4,4,4, 5,4,4, 1,2,4, 1,4,4, 1,6,4, 0,4,4,
            2,4,2, 2,4,6, 5,4,2, 5,4,6, 3,2,4, 3,6,4, 4,2,4, 4,6,4,
            2,4,4, 3,4,4, 4,4,4, 5,4,4, 0,4,2, 0,4,6, 1,2,4, 1,6,4,
            2,4,2, 2,4,6, 5,4,2, 5,4,6, 3,2,4, 3,6,4, 4,2,4, 4,6,4
        ];
    }
}
