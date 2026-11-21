// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static partial class DtsHuffmanTables
    {
        public static ReadOnlySpan<int> A7 =>
        [
            0, 1, 0, 1, 3, 6, -1, 3, 5, 2, 3, 4, -2, 4, 14, 3,
            5, 31, -3, 5, 30,
        ];
        public static ReadOnlySpan<int> B7 =>
        [
            0, 2, 3, 1, 2, 1, -1, 2, 0, 2, 3, 4, -2, 4, 11, 3,
            5, 21, -3, 5, 20,
        ];
        public static ReadOnlySpan<int> C7 =>
        [
            0, 2, 3, 1, 2, 2, -1, 2, 1, 2, 4, 3, -2, 4, 2, 3,
            4, 1, -3, 4, 0,
        ];
    }
}
