// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1ObmcMask
    {
        internal static ReadOnlySpan<byte> ForLength(int length) => length switch
        {
            2 => [45, 64],
            4 => [39, 50, 59, 64],
            8 => [36, 42, 48, 53, 57, 61, 64, 64],
            16 => [34, 37, 40, 43, 46, 49, 52, 54, 56, 58, 60, 61, 64, 64, 64, 64],
            32 => [33, 35, 36, 38, 40, 41, 43, 44, 45, 47, 48, 50, 51, 52, 53, 55,
                56, 57, 58, 59, 60, 60, 61, 62, 64, 64, 64, 64, 64, 64, 64, 64],
            _ => throw new InvalidDataException("The AV1 overlap mask length is invalid.")
        };
    }
}
