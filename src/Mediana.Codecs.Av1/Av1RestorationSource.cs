// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly ref struct Av1RestorationSource(ReadOnlySpan<byte> rows, ReadOnlySpan<byte> boundaries, int stride, int width, int height, int firstRow, int stripe, int stripeSize)
    {
        private readonly ReadOnlySpan<byte> originalRows = rows;
        private readonly ReadOnlySpan<byte> savedBoundaries = boundaries;

        internal int Center(int x, int y) => originalRows[(y - firstRow + 3) * stride + x];

        internal int Sample(int x, int y)
        {
            x = Math.Clamp(x, 0, width - 1);
            y = Math.Clamp(y, 0, height - 1);
            int start = stripe * stripeSize - stripeSize / 8;
            int end = start + stripeSize;
            if (y < start)
            {
                int line = Math.Max(y, start - 2) - start + 2;
                return savedBoundaries[((stripe - 1) * 4 + line) * stride + x];
            }
            if (y >= end)
            {
                int line = Math.Min(y, end + 1) - end + 2;
                return savedBoundaries[(stripe * 4 + line) * stride + x];
            }
            return Center(x, y);
        }
    }
}
