// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static partial class DtsHuffmanTables
    {
        public static ReadOnlySpan<int> A5 =>
        [
            0, 1, 0, 1, 2, 2, -1, 3, 6, 2, 4, 14, -2, 4, 15,
        ];
        public static ReadOnlySpan<int> B5 =>
        [
            0, 2, 2, 1, 2, 0, -1, 2, 1, 2, 3, 6, -2, 3, 7,
        ];
        public static ReadOnlySpan<int> C5 =>
        [
            0, 1, 0, 1, 3, 4, -1, 3, 5, 2, 3, 6, -2, 3, 7,
        ];
    }
}
