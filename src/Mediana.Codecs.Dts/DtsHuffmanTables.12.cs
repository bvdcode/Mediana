// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static partial class DtsHuffmanTables
    {
        public static ReadOnlySpan<int> A12 =>
        [
            1, 1, 0, 2, 2, 2, 3, 3, 6, 4, 4, 14, 5, 5, 30, 6,
            6, 62, 7, 8, 255, 8, 8, 254, 9, 9, 507, 10, 9, 506, 11, 9,
            505, 12, 9, 504,
        ];
        public static ReadOnlySpan<int> B12 =>
        [
            1, 1, 1, 2, 2, 0, 3, 3, 2, 4, 5, 15, 5, 5, 12, 6,
            6, 29, 7, 7, 57, 8, 7, 56, 9, 7, 55, 10, 7, 54, 11, 7,
            53, 12, 7, 52,
        ];
        public static ReadOnlySpan<int> C12 =>
        [
            1, 2, 0, 2, 3, 7, 3, 3, 5, 4, 3, 4, 5, 3, 2, 6,
            4, 13, 7, 4, 12, 8, 4, 6, 9, 5, 15, 10, 6, 29, 11, 7,
            57, 12, 7, 56,
        ];
        public static ReadOnlySpan<int> D12 =>
        [
            1, 2, 3, 2, 2, 2, 3, 2, 0, 4, 3, 2, 5, 4, 6, 6,
            5, 14, 7, 6, 30, 8, 7, 62, 9, 8, 126, 10, 9, 254, 11, 10,
            511, 12, 10, 510,
        ];
        public static ReadOnlySpan<int> E12 =>
        [
            1, 1, 1, 2, 2, 0, 3, 3, 2, 4, 4, 6, 5, 5, 14, 6,
            7, 63, 7, 7, 61, 8, 8, 124, 9, 8, 121, 10, 8, 120, 11, 9,
            251, 12, 9, 250,
        ];
    }
}
