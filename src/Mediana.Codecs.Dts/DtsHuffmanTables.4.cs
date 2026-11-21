// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static partial class DtsHuffmanTables
    {
        public static ReadOnlySpan<int> A4 =>
        [
            0, 1, 0, 1, 2, 2, 2, 3, 6, 3, 3, 7,
        ];
        public static ReadOnlySpan<int> B4 =>
        [
            0, 2, 2, 1, 3, 6, 2, 3, 7, 3, 1, 0,
        ];
        public static ReadOnlySpan<int> C4 =>
        [
            0, 3, 6, 1, 3, 7, 2, 1, 0, 3, 2, 2,
        ];
        public static ReadOnlySpan<int> D4 =>
        [
            0, 2, 0, 1, 2, 1, 2, 2, 2, 3, 2, 3,
        ];
    }
}
