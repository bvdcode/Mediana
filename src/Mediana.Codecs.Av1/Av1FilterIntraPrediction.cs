// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1FilterIntraPrediction
    {
        internal static void Predict<T>(Span<T> destination, int stride, int width, int height, int visibleWidth, int visibleHeight,
            ReadOnlySpan<T> above, ReadOnlySpan<T> left, int mode, int bitDepth = 8) where T : unmanaged, IBinaryInteger<T>
        {
            if ((uint)mode >= 5 || width > 32 || height > 32)
            {
                throw new InvalidDataException("The AV1 recursive intra filter geometry or mode is invalid.");
            }
            ReadOnlySpan<sbyte> taps = Taps.Slice(mode * 56, 56);
            int maximum = (1 << bitDepth) - 1;
            Span<T> rows = stackalloc T[width * 3];
            Span<T> previous = rows[..width];
            Span<T> first = rows.Slice(width, width);
            Span<T> second = rows.Slice(width * 2, width);
            above.Slice(1, width).CopyTo(previous);
            Span<int> neighbors = stackalloc int[7];
            for (int y = 0; y < height; y += 2)
            {
                for (int x = 0; x < width; x += 4)
                {
                    neighbors[0] = int.CreateTruncating(x != 0 ? previous[x - 1] : y == 0 ? above[0] : left[y]);
                    for (int index = 1; index < 5; index++) { neighbors[index] = int.CreateTruncating(previous[x + index - 1]); }
                    neighbors[5] = int.CreateTruncating(x == 0 ? left[y + 1] : first[x - 1]);
                    neighbors[6] = int.CreateTruncating(x == 0 ? left[y + 2] : second[x - 1]);
                    for (int index = 0; index < 8; index++)
                    {
                        int sum = 0;
                        for (int sample = 0; sample < 7; sample++) { sum += taps[index * 7 + sample] * neighbors[sample]; }
                        T value = T.CreateTruncating(Math.Clamp((sum + 8) >> 4, 0, maximum));
                        if (index < 4) { first[x + index] = value; }
                        else { second[x + index - 4] = value; }
                    }
                }
                if (y < visibleHeight) { first[..visibleWidth].CopyTo(destination.Slice(y * stride, visibleWidth)); }
                if (y + 1 < visibleHeight) { second[..visibleWidth].CopyTo(destination.Slice((y + 1) * stride, visibleWidth)); }
                second.CopyTo(previous);
            }
        }

        private static ReadOnlySpan<sbyte> Taps =>
        [
            -6, 10, 0, 0, 0, 12, 0, -5, 2, 10, 0, 0, 9, 0,
            -3, 1, 1, 10, 0, 7, 0, -3, 1, 1, 2, 10, 5, 0,
            -4, 6, 0, 0, 0, 2, 12, -3, 2, 6, 0, 0, 2, 9,
            -3, 2, 2, 6, 0, 2, 7, -3, 1, 2, 2, 6, 3, 5,
            -10, 16, 0, 0, 0, 10, 0, -6, 0, 16, 0, 0, 6, 0,
            -4, 0, 0, 16, 0, 4, 0, -2, 0, 0, 0, 16, 2, 0,
            -10, 16, 0, 0, 0, 0, 10, -6, 0, 16, 0, 0, 0, 6,
            -4, 0, 0, 16, 0, 0, 4, -2, 0, 0, 0, 16, 0, 2,
            -8, 8, 0, 0, 0, 16, 0, -8, 0, 8, 0, 0, 16, 0,
            -8, 0, 0, 8, 0, 16, 0, -8, 0, 0, 0, 8, 16, 0,
            -4, 4, 0, 0, 0, 0, 16, -4, 0, 4, 0, 0, 0, 16,
            -4, 0, 0, 4, 0, 0, 16, -4, 0, 0, 0, 4, 0, 16,
            -2, 8, 0, 0, 0, 10, 0, -1, 3, 8, 0, 0, 6, 0,
            -1, 2, 3, 8, 0, 4, 0, 0, 1, 2, 3, 8, 2, 0,
            -1, 4, 0, 0, 0, 3, 10, -1, 3, 4, 0, 0, 4, 6,
            -1, 2, 3, 4, 0, 4, 4, -1, 2, 2, 3, 4, 3, 3,
            -12, 14, 0, 0, 0, 14, 0, -10, 0, 14, 0, 0, 12, 0,
            -9, 0, 0, 14, 0, 11, 0, -8, 0, 0, 0, 14, 10, 0,
            -10, 12, 0, 0, 0, 0, 14, -9, 1, 12, 0, 0, 0, 12,
            -8, 0, 0, 12, 0, 1, 11, -7, 0, 0, 1, 12, 1, 9,
        ];
    }
}
