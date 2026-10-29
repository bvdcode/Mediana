// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264BPartitionLayout
    {
        public static int Count(int type) => type switch
        {
            0 or 22 => 4,
            1 or 2 or 3 => 1,
            >= 4 and <= 21 => 2,
            _ => throw new InvalidDataException("Invalid H.264 B macroblock type.")
        };

        public static H264BPartition Get(int type, int part, int subtype)
        {
            if ((uint)part >= (uint)Count(type))
            {
                throw new ArgumentOutOfRangeException(nameof(part));
            }
            if (type is 0 or 22)
            {
                (int width, int height, H264BPredictionMode mode) = type == 0 ? (8, 8, H264BPredictionMode.Direct) : Subtype(subtype);
                return new H264BPartition(part % 2 * 8, part / 2 * 8, width, height, mode);
            }
            if (type <= 3)
            {
                H264BPredictionMode mode = type switch
                {
                    1 => H264BPredictionMode.L0,
                    2 => H264BPredictionMode.L1,
                    3 => H264BPredictionMode.Bi,
                    _ => throw new InvalidDataException("Invalid H.264 B prediction mode.")
                };
                return new H264BPartition(0, 0, 16, 16, mode);
            }
            (H264BPredictionMode first, H264BPredictionMode second) = ((type - 4) / 2) switch
            {
                0 => (H264BPredictionMode.L0, H264BPredictionMode.L0),
                1 => (H264BPredictionMode.L1, H264BPredictionMode.L1),
                2 => (H264BPredictionMode.L0, H264BPredictionMode.L1),
                3 => (H264BPredictionMode.L1, H264BPredictionMode.L0),
                4 => (H264BPredictionMode.L0, H264BPredictionMode.Bi),
                5 => (H264BPredictionMode.L1, H264BPredictionMode.Bi),
                6 => (H264BPredictionMode.Bi, H264BPredictionMode.L0),
                7 => (H264BPredictionMode.Bi, H264BPredictionMode.L1),
                8 => (H264BPredictionMode.Bi, H264BPredictionMode.Bi),
                _ => throw new InvalidDataException("Invalid H.264 B prediction mode.")
            };
            return (type % 2) switch
            {
                0 => new H264BPartition(0, part * 8, 16, 8, part == 0 ? first : second),
                1 => new H264BPartition(part * 8, 0, 8, 16, part == 0 ? first : second),
                _ => throw new InvalidDataException("Invalid H.264 B partition orientation.")
            };
        }

        private static (int Width, int Height, H264BPredictionMode Mode) Subtype(int subtype) => subtype switch
        {
            0 => (8, 8, H264BPredictionMode.Direct),
            1 => (8, 8, H264BPredictionMode.L0),
            2 => (8, 8, H264BPredictionMode.L1),
            3 => (8, 8, H264BPredictionMode.Bi),
            4 => (8, 4, H264BPredictionMode.L0),
            5 => (4, 8, H264BPredictionMode.L0),
            6 => (8, 4, H264BPredictionMode.L1),
            7 => (4, 8, H264BPredictionMode.L1),
            8 => (8, 4, H264BPredictionMode.Bi),
            9 => (4, 8, H264BPredictionMode.Bi),
            10 => (4, 4, H264BPredictionMode.L0),
            11 => (4, 4, H264BPredictionMode.L1),
            12 => (4, 4, H264BPredictionMode.Bi),
            _ => throw new InvalidDataException("Invalid H.264 B sub-macroblock type.")
        };

        public static bool Uses(H264BPredictionMode mode, H264ReferenceList list) => (mode, list) switch
        {
            (H264BPredictionMode.Direct, H264ReferenceList.L0 or H264ReferenceList.L1) => false,
            (H264BPredictionMode.L0, H264ReferenceList.L0) or (H264BPredictionMode.L1, H264ReferenceList.L1) => true,
            (H264BPredictionMode.L0, H264ReferenceList.L1) or (H264BPredictionMode.L1, H264ReferenceList.L0) => false,
            (H264BPredictionMode.Bi, H264ReferenceList.L0 or H264ReferenceList.L1) => true,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
}
