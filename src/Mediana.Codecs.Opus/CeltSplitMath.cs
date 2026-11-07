// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Opus
{
    /// <summary>Integer approximations used by CELT's bitstream-dependent split allocation.</summary>
    internal static class CeltSplitMath
    {
        public static int Cosine(int angle)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(angle, 64);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(angle, 16320);
            int square = (4096 + angle * angle) >> 13;
            return 1 + (32767 - square) + Multiply(square, -7651 + Multiply(square, 8277 + Multiply(-626, square)));
        }

        public static int Log2Tangent(int sine, int cosine)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sine);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(sine, 32767);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cosine);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cosine, 32767);
            int sineLog = BitOperations.Log2((uint)sine) + 1;
            int cosineLog = BitOperations.Log2((uint)cosine) + 1;
            sine <<= 15 - sineLog;
            cosine <<= 15 - cosineLog;
            return (sineLog - cosineLog) * 2048 + Multiply(sine, Multiply(sine, -2597) + 7932) -
                Multiply(cosine, Multiply(cosine, -2597) + 7932);
        }

        public static int Multiply(int left, int right) => (16384 + (short)left * (short)right) >> 15;
    }
}
