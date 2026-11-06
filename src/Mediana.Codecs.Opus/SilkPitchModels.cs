// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkPitchModels
    {
        public static ReadOnlySpan<byte> LagHigh =>
        [
            253, 250, 244, 233, 212, 182, 150, 131, 120, 110, 98, 85, 72, 60, 49, 40,
            32, 25, 19, 15, 13, 11, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0
        ];

        public static ReadOnlySpan<byte> Delta =>
        [
            210, 208, 206, 203, 199, 193, 183, 168, 142, 104, 74, 52, 37, 27, 20, 14,
            10, 6, 4, 2, 0
        ];

        public static ReadOnlySpan<byte> NarrowShort =>
        [
            113, 63, 0
        ];

        public static ReadOnlySpan<byte> NarrowLong =>
        [
            188, 176, 155, 138, 119, 97, 67, 43, 26, 10, 0
        ];

        public static ReadOnlySpan<byte> WideShort =>
        [
            165, 119, 80, 61, 47, 35, 27, 20, 14, 9, 4, 0
        ];

        public static ReadOnlySpan<byte> WideLong =>
        [
            223, 201, 183, 167, 152, 138, 124, 111, 98, 88, 79, 70, 62, 56, 50, 44,
            39, 35, 31, 27, 24, 21, 18, 16, 14, 12, 10, 8, 6, 4, 3, 2,
            1, 0
        ];

        public static ReadOnlySpan<sbyte> NarrowShortLags => narrowShortLags;

        private static readonly sbyte[] narrowShortLags =
        [
            0, 1, 0, 0, 0, 1
        ];

        public static ReadOnlySpan<sbyte> NarrowLongLags => narrowLongLags;

        private static readonly sbyte[] narrowLongLags =
        [
            0, 2, -1, -1, -1, 0, 0, 1, 1, 0, 1, 0, 1, 0, 0, 0,
            0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            0, 0, -1, 2, 1, 0, 1, 1, 0, 0, -1, -1
        ];

        public static ReadOnlySpan<sbyte> WideShortLags => wideShortLags;

        private static readonly sbyte[] wideShortLags =
        [
            0, 0, 1, -1, 1, -1, 2, -2, 2, -2, 3, -3, 0, 1, 0, 1,
            -1, 2, -1, 2, -2, 3, -2, 3
        ];

        public static ReadOnlySpan<sbyte> WideLongLags => wideLongLags;

        private static readonly sbyte[] wideLongLags =
        [
            0, 0, 1, -1, 0, 1, -1, 0, -1, 1, -2, 2, -2, -2, 2, -3,
            2, 3, -3, -4, 3, -4, 4, 4, -5, 5, -6, -5, 6, -7, 6, 5,
            8, -9, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, -1, 1, 0, 0,
            1, -1, 0, 1, -1, -1, 1, -1, 2, 1, -1, 2, -2, -2, 2, -2,
            2, 2, 3, -3, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0,
            0, 1, -1, 1, 0, 0, 2, 1, -1, 2, -1, -1, 2, -1, 2, 2,
            -1, 3, -2, -2, -2, 3, 0, 1, 0, 0, 1, 0, 1, -1, 2, -1,
            2, -1, 2, 3, -2, 3, -2, -2, 4, 4, -3, 5, -3, -4, 6, -4,
            6, 5, -5, 8, -6, -5, -7, 9
        ];

        public static ReadOnlySpan<byte> ContourModel(SilkFrameMode mode) => (mode.RateKhz, mode.Subframes) switch
        {
            (8, 2) => NarrowShort,
            (8, 4) => NarrowLong,
            (12 or 16, 2) => WideShort,
            (12 or 16, 4) => WideLong,
            _ => throw new ArgumentException("The SILK frame mode is uninitialized.", nameof(mode))
        };

        public static ReadOnlySpan<sbyte> ContourLags(SilkFrameMode mode) => (mode.RateKhz, mode.Subframes) switch
        {
            (8, 2) => NarrowShortLags,
            (8, 4) => NarrowLongLags,
            (12 or 16, 2) => WideShortLags,
            (12 or 16, 4) => WideLongLags,
            _ => throw new ArgumentException("The SILK frame mode is uninitialized.", nameof(mode))
        };
    }
}
