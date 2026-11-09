// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1SelfGuidedParameters
    {
        private static ReadOnlySpan<byte> Values =>
        [
            2, 12, 1, 4, 2, 15, 1, 6, 2, 18, 1, 8, 2, 21, 1, 9,
            2, 24, 1, 10, 2, 29, 1, 11, 2, 36, 1, 12, 2, 45, 1, 13,
            2, 56, 1, 14, 2, 68, 1, 15, 0, 0, 1, 5, 0, 0, 1, 8,
            0, 0, 1, 11, 0, 0, 1, 14, 2, 30, 0, 0, 2, 75, 0, 0
        ];
        internal static int Radius(int set, int pass) => Values[set * 4 + pass * 2];
        internal static int Epsilon(int set, int pass) => Values[set * 4 + pass * 2 + 1];
    }
}
