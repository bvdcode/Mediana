// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkLtpModels
    {
        public static ReadOnlySpan<byte> Model0 =>
        [
            71, 56, 43, 30, 21, 12, 6, 0
        ];

        public static ReadOnlySpan<sbyte> Taps0 => taps0;

        private static readonly sbyte[] taps0 =
        [
            4, 6, 24, 7, 5, 0, 0, 2, 0, 0, 12, 28, 41, 13, -4, -9,
            15, 42, 25, 14, 1, -2, 62, 41, -9, -10, 37, 65, -4, 3, -6, 4,
            66, 7, -8, 16, 14, 38, -3, 33
        ];

        public static ReadOnlySpan<byte> Model1 =>
        [
            199, 165, 144, 124, 109, 96, 84, 71, 61, 51, 42, 32, 23, 15, 8, 0
        ];

        public static ReadOnlySpan<sbyte> Taps1 => taps1;

        private static readonly sbyte[] taps1 =
        [
            13, 22, 39, 23, 12, -1, 36, 64, 27, -6, -7, 10, 55, 43, 17, 1,
            1, 8, 1, 1, 6, -11, 74, 53, -9, -12, 55, 76, -12, 8, -3, 3,
            93, 27, -4, 26, 39, 59, 3, -8, 2, 0, 77, 11, 9, -8, 22, 44,
            -6, 7, 40, 9, 26, 3, 9, -7, 20, 101, -7, 4, 3, -8, 42, 26,
            0, -15, 33, 68, 2, 23, -2, 55, 46, -2, 15, 3, -1, 21, 16, 41
        ];

        public static ReadOnlySpan<byte> Model2 =>
        [
            241, 225, 211, 199, 187, 175, 164, 153, 142, 132, 123, 114, 105, 96, 88, 80,
            72, 64, 57, 50, 44, 38, 33, 29, 24, 20, 16, 12, 9, 5, 2, 0
        ];

        public static ReadOnlySpan<sbyte> Taps2 => taps2;

        private static readonly sbyte[] taps2 =
        [
            -6, 27, 61, 39, 5, -11, 42, 88, 4, 1, -2, 60, 65, 6, -4, -1,
            -5, 73, 56, 1, -9, 19, 94, 29, -9, 0, 12, 99, 6, 4, 8, -19,
            102, 46, -13, 3, 2, 13, 3, 2, 9, -21, 84, 72, -18, -11, 46, 104,
            -22, 8, 18, 38, 48, 23, 0, -16, 70, 83, -21, 11, 5, -11, 117, 22,
            -8, -6, 23, 117, -12, 3, 3, -8, 95, 28, 4, -10, 15, 77, 60, -15,
            -1, 4, 124, 2, -4, 3, 38, 84, 24, -25, 2, 13, 42, 13, 31, 21,
            -4, 56, 46, -1, -1, 35, 79, -13, 19, -7, 65, 88, -9, -14, 20, 4,
            81, 49, -29, 20, 0, 75, 3, -17, 5, -9, 44, 92, -8, 1, -3, 22,
            69, 31, -6, 95, 41, -12, 5, 39, 67, 16, -4, 1, 0, -6, 120, 55,
            -36, -13, 44, 122, 4, -24, 81, 5, 11, 3, 7, 2, 0, 9, 10, 88
        ];

        public static ReadOnlySpan<byte> Probability(int codebook) => codebook switch
        {
            0 => Model0,
            1 => Model1,
            2 => Model2,
            _ => throw new ArgumentOutOfRangeException(nameof(codebook))
        };

        public static ReadOnlySpan<sbyte> Coefficients(int codebook) => codebook switch
        {
            0 => Taps0,
            1 => Taps1,
            2 => Taps2,
            _ => throw new ArgumentOutOfRangeException(nameof(codebook))
        };
    }
}
