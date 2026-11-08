// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1CompoundWeights(int First, int Second)
    {
        internal static Av1CompoundWeights Average => new(8, 8);

        internal static Av1CompoundWeights FromDistances(int firstDistance, int secondDistance)
        {
            int d0 = Math.Min(31, Math.Abs(secondDistance));
            int d1 = Math.Min(31, Math.Abs(firstDistance));
            int order = d0 <= d1 ? 1 : 0;
            ReadOnlySpan<byte> thresholds = [2, 3, 2, 5, 2, 7];
            ReadOnlySpan<byte> weights = [9, 7, 11, 5, 12, 4, 13, 3];
            int selected = 3;
            if (d0 != 0 && d1 != 0)
            {
                for (int index = 0; index < 3; index++)
                {
                    int first = d0 * thresholds[index * 2 + order];
                    int second = d1 * thresholds[index * 2 + 1 - order];
                    if (order != 0 ? first > second : first < second) { selected = index; break; }
                }
            }
            return new(weights[selected * 2 + order], weights[selected * 2 + 1 - order]);
        }
    }
}
