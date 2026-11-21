// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static partial class DtsHuffmanTables
    {
        public static ReadOnlySpan<int> A13 =>
        [
            0, 1, 0, 1, 3, 4, -1, 4, 15, 2, 4, 13, -2, 4, 12, 3,
            4, 10, -3, 5, 29, 4, 5, 22, -4, 6, 57, 5, 6, 47, -5, 6,
            46, 6, 7, 113, -6, 7, 112,
        ];
        public static ReadOnlySpan<int> B13 =>
        [
            0, 2, 0, 1, 3, 6, -1, 3, 5, 2, 3, 2, -2, 4, 15, 3,
            4, 9, -3, 4, 7, 4, 4, 6, -4, 5, 29, 5, 5, 17, -5, 5,
            16, 6, 6, 57, -6, 6, 56,
        ];
        public static ReadOnlySpan<int> C13 =>
        [
            0, 3, 5, 1, 3, 4, -1, 3, 3, 2, 3, 2, -2, 3, 0, 3,
            4, 15, -3, 4, 14, 4, 4, 12, -4, 4, 3, 5, 5, 27, -5, 5,
            26, 6, 5, 5, -6, 5, 4,
        ];
    }
}
