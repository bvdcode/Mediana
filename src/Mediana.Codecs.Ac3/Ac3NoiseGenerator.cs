// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3
{
    internal static class Ac3NoiseGenerator
    {
        public static double Next(ref uint state)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state >> 8) / 8388608.0 - 1;
        }
    }
}
