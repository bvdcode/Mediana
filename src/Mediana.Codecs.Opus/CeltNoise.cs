// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltNoise
    {
        public static uint Next(uint seed) => unchecked(1664525 * seed + 1013904223);
    }
}
