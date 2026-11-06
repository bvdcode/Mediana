// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltBandTables
    {
        public const int BandCount = 21;
        public const int QualityCount = 11;
        public const int MaximumTimeBlocks = 16;
        public const int MaximumBandDimensions = 176;

        public static ReadOnlySpan<byte> Boundaries =>
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 34, 40, 48, 60, 78, 100];

        public static ReadOnlySpan<byte> WidthLog2Eighths =>
            [0, 0, 0, 0, 0, 0, 0, 0, 8, 8, 8, 8, 16, 16, 16, 21, 21, 24, 29, 34, 36];

        public static ReadOnlySpan<byte> StereoReservationEighths =>
            [0, 8, 13, 16, 19, 21, 23, 24, 26, 27, 28, 29, 30, 31, 32, 32, 33, 34, 34, 35, 36, 36];

        public static int Width(int band) => Boundaries[band + 1] - Boundaries[band];

        public static int MaximumAllocation(CeltBandMode mode, int band) =>
            (Caps[(2 * mode.FrameShift + mode.Channels - 1) * BandCount + band] + 64) *
            mode.Channels * (Width(band) << mode.FrameShift) / 4;

        public static int StaticAllocation(CeltBandMode mode, int band, int quality) =>
            (mode.Channels * Width(band) * Weights[quality * BandCount + band] << mode.FrameShift) / 4;

        private static ReadOnlySpan<byte> Weights =>
        [
              0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
             90, 80, 75, 69, 63, 56, 49, 40, 34, 29, 20, 18, 10,  0,  0,  0,  0,  0,  0,  0,  0,
            110,100, 90, 84, 78, 71, 65, 58, 51, 45, 39, 32, 26, 20, 12,  0,  0,  0,  0,  0,  0,
            118,110,103, 93, 86, 80, 75, 70, 65, 59, 53, 47, 40, 31, 23, 15,  4,  0,  0,  0,  0,
            126,119,112,104, 95, 89, 83, 78, 72, 66, 60, 54, 47, 39, 32, 25, 17, 12,  1,  0,  0,
            134,127,120,114,103, 97, 91, 85, 78, 72, 66, 60, 54, 47, 41, 35, 29, 23, 16, 10,  1,
            144,137,130,124,113,107,101, 95, 88, 82, 76, 70, 64, 57, 51, 45, 39, 33, 26, 15,  1,
            152,145,138,132,123,117,111,105, 98, 92, 86, 80, 74, 67, 61, 55, 49, 43, 36, 20,  1,
            162,155,148,142,133,127,121,115,108,102, 96, 90, 84, 77, 71, 65, 59, 53, 46, 30,  1,
            172,165,158,152,143,137,131,125,118,112,106,100, 94, 87, 81, 75, 69, 63, 56, 45, 20,
            200,200,200,200,200,200,200,200,198,193,188,183,178,173,168,163,158,153,148,129,104
        ];

        private static ReadOnlySpan<byte> Caps =>
        [
            224,224,224,224,224,224,224,224,160,160,160,160,185,185,185,178,178,168,134,61,37,
            224,224,224,224,224,224,224,224,240,240,240,240,207,207,207,198,198,183,144,66,40,
            160,160,160,160,160,160,160,160,185,185,185,185,193,193,193,183,183,172,138,64,38,
            240,240,240,240,240,240,240,240,207,207,207,207,204,204,204,193,193,180,143,66,40,
            185,185,185,185,185,185,185,185,193,193,193,193,193,193,193,183,183,172,138,65,39,
            207,207,207,207,207,207,207,207,204,204,204,204,201,201,201,188,188,176,141,66,40,
            193,193,193,193,193,193,193,193,193,193,193,193,194,194,194,184,184,173,139,65,39,
            204,204,204,204,204,204,204,204,201,201,201,201,198,198,198,187,187,175,140,66,40
        ];
    }
}
