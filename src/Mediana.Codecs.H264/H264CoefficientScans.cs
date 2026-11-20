// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Inverse coefficient scan positions from H.264 Tables 8-13 and 8-14.</summary>
    internal static class H264CoefficientScans
    {
        public static ReadOnlySpan<int> Frame4x4 => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        private static ReadOnlySpan<int> Field4x4 => [0, 4, 1, 8, 12, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15];
        private static ReadOnlySpan<int> Frame8x8 =>
        [
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        ];
        private static ReadOnlySpan<int> Field8x8 =>
        [
            0, 8, 16, 1, 9, 24, 32, 17, 2, 25, 40, 48, 56, 33, 10, 3,
            18, 41, 49, 57, 26, 11, 4, 19, 34, 42, 50, 58, 27, 12, 5, 20,
            35, 43, 51, 59, 28, 13, 6, 21, 36, 44, 52, 60, 29, 14, 22, 37,
            45, 53, 61, 30, 7, 15, 38, 46, 54, 62, 23, 31, 39, 47, 55, 63,
        ];

        public static ReadOnlySpan<int> FourByFour(bool field) => field ? Field4x4 : Frame4x4;
        public static ReadOnlySpan<int> EightByEight(bool field) => field ? Field8x8 : Frame8x8;
    }
}
