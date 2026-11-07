// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal static class HevcPartitionLayout
    {
        public static int Write(HevcPartitionMode mode, int x, int y, int size, Span<HevcPredictionBlock> output)
        {
            if (output.Length < 4) { throw new ArgumentException("HEVC partition storage requires four entries.", nameof(output)); }
            int firstWidth = size, firstHeight = size, secondX = x, secondY = y, secondWidth = size, secondHeight = size;
            switch (mode)
            {
                case HevcPartitionMode.Whole:
                    output[0] = new(x, y, size, x, y, size, size, 0, mode); return 1;
                case HevcPartitionMode.Quarters:
                    for (int i = 0; i < 4; i++)
                    { output[i] = new(x, y, size, x + (i & 1) * (size / 2), y + (i >> 1) * (size / 2), size / 2, size / 2, i, mode); }
                    return 4;
                case HevcPartitionMode.Horizontal: firstHeight = size / 2; secondHeight = size / 2; secondY += firstHeight; break;
                case HevcPartitionMode.Vertical: firstWidth = size / 2; secondWidth = size / 2; secondX += firstWidth; break;
                case HevcPartitionMode.NarrowTop: firstHeight = size / 4; secondHeight = size * 3 / 4; secondY += firstHeight; break;
                case HevcPartitionMode.NarrowBottom: firstHeight = size * 3 / 4; secondHeight = size / 4; secondY += firstHeight; break;
                case HevcPartitionMode.NarrowLeft: firstWidth = size / 4; secondWidth = size * 3 / 4; secondX += firstWidth; break;
                case HevcPartitionMode.NarrowRight: firstWidth = size * 3 / 4; secondWidth = size / 4; secondX += firstWidth; break;
                default: throw new InvalidDataException("Invalid HEVC prediction partition.");
            }
            output[0] = new(x, y, size, x, y, firstWidth, firstHeight, 0, mode);
            output[1] = new(x, y, size, secondX, secondY, secondWidth, secondHeight, 1, mode); return 2;
        }
    }
}
