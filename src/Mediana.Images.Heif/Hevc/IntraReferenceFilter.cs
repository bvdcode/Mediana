// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Filters already substituted luma borders according to HEVC section 8.4.4.2.3.
    /// </summary>
    internal static class IntraReferenceFilter
    {
        public static void Apply(Span<int> top, Span<int> left, int size, int mode, int bitDepth, bool strongSmoothing)
        {
            int threshold;
            switch (size)
            {
                case 4:
                    return;
                case 8:
                    threshold = 7;
                    break;
                case 16:
                    threshold = 1;
                    break;
                case 32:
                    threshold = 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size));
            }

            if ((mode == 1) || (Math.Min(Math.Abs(mode - 26), Math.Abs(mode - 10)) <= threshold))
            {
                return;
            }
            int end = 2 * size;
            int corner = top[0];
            bool linear = strongSmoothing && (size == 32) &&
                (Math.Abs((corner + top[end]) - (2 * top[size])) < (1 << (bitDepth - 5))) &&
                (Math.Abs((corner + left[end]) - (2 * left[size])) < (1 << (bitDepth - 5)));
            if (linear)
            {
                for (int i = 1; i < end; i++)
                {
                    top[i] = (((end - i) * corner) + (i * top[end]) + 32) >> 6;
                    left[i] = (((end - i) * corner) + (i * left[end]) + 32) >> 6;
                }
            }
            else
            {
                int filteredCorner = (left[1] + (2 * corner) + top[1] + 2) >> 2;
                Smooth(top, end);
                Smooth(left, end);
                top[0] = filteredCorner;
                left[0] = filteredCorner;
            }
        }

        private static void Smooth(Span<int> border, int end)
        {
            int previous = border[0];
            for (int i = 1; i < end; i++)
            {
                int current = border[i];
                border[i] = (previous + (2 * current) + border[i + 1] + 2) >> 2;
                previous = current;
            }
        }
    }
}
