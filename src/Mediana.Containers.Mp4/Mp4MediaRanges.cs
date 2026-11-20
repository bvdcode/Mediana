// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4MediaRanges
    {
        public static bool Contains(IReadOnlyList<Mp4DataRange> ranges, long offset, long length)
        {
            int low = 0;
            int high = ranges.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                Mp4DataRange range = ranges[middle];
                if (offset < range.Offset)
                {
                    high = middle - 1;
                }
                else if (offset - range.Offset > range.Length)
                {
                    low = middle + 1;
                }
                else
                {
                    return range.Contains(offset, length);
                }
            }
            return false;
        }
    }
}
