// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    internal static class CoefficientContexts
    {
        private static readonly byte[] SmallMap = { 0, 1, 4, 5, 2, 3, 4, 5, 6, 6, 8, 8, 7, 7, 8, 8 };
        private static readonly byte[] SignificantMaps = CreateSignificantMaps();

        public static ReadOnlySpan<byte> SignificantGroup(int gx, int gy, int log2Size, int scanIndex,
            int component, ReadOnlySpan<byte> codedGroups)
        {
            int width = 1 << (log2Size - 2);
            int pattern = 0;
            if ((gx + 1) < width)
            {
                pattern += codedGroups[(gy * width) + gx + 1];
            }
            if ((gy + 1) < width)
            {
                pattern += 2 * codedGroups[((gy + 1) * width) + gx];
            }
            if (pattern > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(codedGroups));
            }
            int map = ((((log2Size - 2) * 3) + scanIndex) * 16) + (component == 0 ? 0 : 8) +
                ((gx + gy) == 0 ? 0 : 4) + pattern;
            return SignificantMaps.AsSpan(map * 16, 16);
        }

        private static int Significant(int x, int y, int log2Size, int scanIndex, int component, int pattern)
        {
            int context;
            if (log2Size == 2)
            {
                context = SmallMap[(y * 4) + x];
            }
            else if ((x + y) == 0)
            {
                context = 0;
            }
            else
            {
                int gx = x / 4;
                int gy = y / 4;
                context = LocalSignificant(x & 3, y & 3, pattern);

                if (component == 0)
                {
                    if ((gx + gy) > 0)
                    {
                        context += 3;
                    }
                    context += log2Size == 3 ? (scanIndex == 0 ? 9 : 15) : 21;
                }
                else
                {
                    context += log2Size == 3 ? 9 : 12;
                }
            }

            return component == 0 ? context : 27 + context;
        }

        private static byte[] CreateSignificantMaps()
        {
            byte[] maps = new byte[4 * 3 * 2 * 2 * 4 * 16];
            int index = 0;
            for (int log2Size = 2; log2Size <= 5; log2Size++)
            {
                for (int scanIndex = 0; scanIndex < 3; scanIndex++)
                {
                    ReadOnlySpan<int> scan = CoefficientScan.Block(2, scanIndex);
                    for (int component = 0; component < 2; component++)
                    {
                        for (int origin = 0; origin < 2; origin++)
                        {
                            for (int pattern = 0; pattern < 4; pattern++)
                            {
                                for (int n = 0; n < scan.Length; n++)
                                {
                                    int x = (scan[n] % 4) + (log2Size > 2 ? origin * 4 : 0);
                                    maps[index++] = (byte)Significant(x, scan[n] / 4, log2Size, scanIndex, component, pattern);
                                }
                            }
                        }
                    }
                }
            }
            return maps;
        }

        public static int Group(int gx, int gy, int width, int component, ReadOnlySpan<byte> codedGroups)
        {
            int neighbours = 0;
            if ((gx + 1) < width)
            {
                neighbours += codedGroups[(gy * width) + gx + 1];
            }
            if ((gy + 1) < width)
            {
                neighbours += codedGroups[((gy + 1) * width) + gx];
            }
            return (component == 0 ? 0 : 2) + Math.Min(neighbours, 1);
        }

        private static int LocalSignificant(int px, int py, int pattern)
        {
            switch (pattern)
            {
                case 0:
                    return (px + py) == 0 ? 2 : (px + py) < 3 ? 1 : 0;
                case 1:
                    return py == 0 ? 2 : py == 1 ? 1 : 0;
                case 2:
                    return px == 0 ? 2 : px == 1 ? 1 : 0;
                case 3:
                    return 2;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }
    }
}
