// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal static class HevcCabacInitialization
    {
        private static readonly byte[][][] Values =
        [
            [
                [139, 141, 157],
                [154],
                [184],
                [184],
                [63],
                [153, 138, 138],
                [111, 141],
                [94, 138, 182, 154, 154],
                [154, 154],
                [139, 139],
                [110, 110, 124, 125, 140, 153, 125, 127, 140, 109, 111, 143, 127, 111, 79, 108, 123, 63],
                [110, 110, 124, 125, 140, 153, 125, 127, 140, 109, 111, 143, 127, 111, 79, 108, 123, 63],
                [91, 171, 134, 141],
                [111, 111, 125, 110, 110, 94, 124, 108, 124, 107, 125, 141, 179, 153, 125, 107, 125, 141, 179, 153, 125, 107, 125, 141, 179, 153, 125, 140, 139, 182, 182, 152, 136, 152, 136, 153, 136, 139, 111, 136, 139, 111, 141, 111],
                [140, 92, 137, 138, 140, 152, 138, 139, 153, 74, 149, 92, 139, 107, 122, 152, 140, 179, 166, 182, 140, 227, 122, 197],
                [138, 153, 136, 167, 152, 152],
                [153],
                [200],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []
            ],
            [
                [107, 139, 126],
                [154],
                [154, 139, 154, 154],
                [154],
                [152],
                [124, 138, 94],
                [153, 111],
                [149, 107, 167, 154, 154],
                [154, 154],
                [139, 139],
                [125, 110, 94, 110, 95, 79, 125, 111, 110, 78, 110, 111, 111, 95, 94, 108, 123, 108],
                [125, 110, 94, 110, 95, 79, 125, 111, 110, 78, 110, 111, 111, 95, 94, 108, 123, 108],
                [121, 140, 61, 154],
                [155, 154, 139, 153, 139, 123, 123, 63, 153, 166, 183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154, 170, 153, 123, 123, 107, 121, 107, 121, 167, 151, 183, 140, 151, 183, 140, 140, 140],
                [154, 196, 196, 167, 154, 152, 167, 182, 182, 134, 149, 136, 153, 121, 136, 137, 169, 194, 166, 167, 154, 167, 137, 182],
                [107, 167, 91, 122, 107, 167],
                [153],
                [185],
                [197, 185, 201],
                [149],
                [79],
                [110],
                [122],
                [95, 79, 63, 31, 31],
                [153, 153],
                [168],
                [140, 198]
            ],
            [
                [107, 139, 126],
                [154],
                [154, 139, 154, 154],
                [183],
                [152],
                [224, 167, 122],
                [153, 111],
                [149, 92, 167, 154, 154],
                [154, 154],
                [139, 139],
                [125, 110, 124, 110, 95, 94, 125, 111, 111, 79, 125, 126, 111, 111, 79, 108, 123, 93],
                [125, 110, 124, 110, 95, 94, 125, 111, 111, 79, 125, 126, 111, 111, 79, 108, 123, 93],
                [121, 140, 61, 154],
                [170, 154, 139, 153, 139, 123, 123, 63, 124, 166, 183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154, 170, 153, 138, 138, 122, 121, 122, 121, 167, 151, 183, 140, 151, 183, 140, 140, 140],
                [154, 196, 167, 167, 154, 152, 167, 182, 182, 134, 149, 136, 153, 121, 136, 122, 169, 208, 166, 167, 154, 152, 167, 182],
                [107, 167, 91, 107, 107, 167],
                [153],
                [160],
                [197, 185, 201],
                [134],
                [79],
                [154],
                [137],
                [95, 79, 63, 31, 31],
                [153, 153],
                [168],
                [169, 198]
            ]
        ];
        private static readonly int[][] Offsets = CreateOffsets();

        public static ReadOnlyMemory<int> GetOffsets(int initializationType)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(initializationType);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(initializationType, 2);
            return Offsets[initializationType];
        }

        public static void Initialize(Span<CabacContext> contexts, int initializationType, int qp)
        {
            int offset = 0;
            foreach (byte[] group in Values[initializationType])
            {
                foreach (byte value in group) { contexts[offset++] = CabacContext.Initialize(value, qp); }
            }
            if (offset != contexts.Length) { throw new ArgumentException("The HEVC context storage has an invalid size.", nameof(contexts)); }
        }

        private static int[][] CreateOffsets()
        {
            int[][] offsets = new int[Values.Length][];
            for (int type = 0; type < Values.Length; type++)
            {
                byte[][] groups = Values[type];
                offsets[type] = new int[groups.Length + 1];
                for (int group = 0; group < groups.Length; group++) { offsets[type][group + 1] = offsets[type][group] + groups[group].Length; }
            }
            return offsets;
        }
    }
}
