// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static partial class DtsHuffmanTables
    {
        public static ReadOnlySpan<int> A9 =>
        [
            0, 1, 0, 1, 3, 7, -1, 3, 5, 2, 4, 13, -2, 4, 9, 3,
            4, 8, -3, 5, 25, 4, 6, 49, -4, 6, 48,
        ];
        public static ReadOnlySpan<int> B9 =>
        [
            0, 2, 2, 1, 2, 0, -1, 3, 7, 2, 3, 3, -2, 3, 2, 3,
            5, 27, -3, 5, 26, 4, 5, 25, -4, 5, 24,
        ];
        public static ReadOnlySpan<int> C9 =>
        [
            0, 2, 2, 1, 2, 0, -1, 3, 7, 2, 3, 6, -2, 3, 2, 3,
            4, 6, -3, 5, 15, 4, 6, 29, -4, 6, 28,
        ];
    }
}
