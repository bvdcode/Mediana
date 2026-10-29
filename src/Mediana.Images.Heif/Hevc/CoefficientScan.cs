// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Maps HEVC scan positions to row-major offsets, with x increasing across a row.
    /// </summary>
    internal static class CoefficientScan
    {
        private static readonly int[][] BlockOrders = CreateBlockOrders();
        private static readonly int[] Diagonal4 = CreateDiagonal(4);
        private static readonly int[] Diagonal8 = CreateDiagonal(8);

        public static ReadOnlySpan<int> Block(int log2Size, int scanIndex)
        {
            if ((log2Size < 2) || (log2Size > 5) || (scanIndex < 0) || (scanIndex > 2))
            {
                throw new ArgumentOutOfRangeException(nameof(log2Size));
            }

            return BlockOrders[((log2Size - 2) * 3) + scanIndex];
        }

        public static ReadOnlySpan<int> Matrix(int size)
        {
            switch (size)
            {
                case 4:
                    return Diagonal4;
                case 8:
                    return Diagonal8;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size));
            }
        }

        private static int[][] CreateBlockOrders()
        {
            int[][] orders = new int[12][];
            for (int log2Size = 2; log2Size <= 5; log2Size++)
            {
                int size = 1 << log2Size;
                int groups = size / 4;
                for (int scan = 0; scan < 3; scan++)
                {
                    int[] order = new int[size * size];
                    int[] groupOrder = CreateOrder(groups, scan);
                    int[] within = CreateOrder(4, scan);
                    for (int group = 0; group < groupOrder.Length; group++)
                    {
                        int gx = groupOrder[group] % groups;
                        int gy = groupOrder[group] / groups;
                        for (int i = 0; i < 16; i++)
                        {
                            order[(group * 16) + i] = (((gy * 4) + (within[i] / 4)) * size) + (gx * 4) + (within[i] % 4);
                        }
                    }

                    orders[((log2Size - 2) * 3) + scan] = order;
                }
            }

            return orders;
        }

        private static int[] CreateOrder(int size, int scan)
        {
            if (scan == 0)
            {
                return CreateDiagonal(size);
            }
            int[] result = new int[size * size];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = scan == 1 ? i : ((i % size) * size) + (i / size);
            }

            return result;
        }

        private static int[] CreateDiagonal(int size)
        {
            int[] result = new int[size * size];
            int index = 0;
            for (int sum = 0; sum <= (2 * (size - 1)); sum++)
            {
                for (int x = Math.Max(0, (sum - size) + 1); x <= Math.Min(size - 1, sum); x++)
                {
                    result[index++] = ((sum - x) * size) + x;
                }
            }

            return result;
        }
    }
}
