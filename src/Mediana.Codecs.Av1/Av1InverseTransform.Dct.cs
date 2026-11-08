// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1InverseTransform
    {
        private static void Dct(Span<int> values)
        {
            if (values.Length == 4)
            {
                Dct4(values);
                return;
            }
            int n = BitOperations.Log2((uint)values.Length);
            Span<int> copy = stackalloc int[64];
            values.CopyTo(copy);
            for (int index = 0; index < values.Length; index++) { values[index] = copy[Reverse(n, index)]; }
            if (n == 6)
            {
                for (int i = 0; i < 16; i++) { Rotate(values, 32 + i, 63 - i, 63 - 4 * Reverse(4, i), false); }
            }
            if (n >= 5)
            {
                for (int i = 0; i < 8; i++) { Rotate(values, 16 + i, 31 - i, 6 + (Reverse(3, 7 - i) << 3), false); }
            }
            if (n == 6)
            {
                for (int i = 0; i < 16; i++) { Hadamard(values, 32 + i * 2, 33 + i * 2, (i & 1) != 0); }
            }
            if (n >= 4)
            {
                for (int i = 0; i < 4; i++) { Rotate(values, 8 + i, 15 - i, 12 + (Reverse(2, 3 - i) << 4), false); }
            }
            if (n >= 5)
            {
                for (int i = 0; i < 8; i++) { Hadamard(values, 16 + 2 * i, 17 + 2 * i, (i & 1) != 0); }
            }
            if (n == 6)
            {
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 2; j++) { Rotate(values, 62 - i * 4 - j, 33 + i * 4 + j, 60 - 16 * Reverse(2, i) + 64 * j, true); }
                }
            }
            if (n >= 3)
            {
                for (int i = 0; i < 2; i++) { Rotate(values, 4 + i, 7 - i, 56 - 32 * i, false); }
            }
            if (n >= 4)
            {
                for (int i = 0; i < 4; i++) { Hadamard(values, 8 + 2 * i, 9 + 2 * i, (i & 1) != 0); }
            }
            if (n >= 5)
            {
                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 2; j++) { Rotate(values, 30 - 4 * i - j, 17 + 4 * i + j, 24 + (j << 6) + ((1 - i) << 5), true); }
                }
            }
            if (n == 6)
            {
                for (int i = 0; i < 8; i++)
                {
                    for (int j = 0; j < 2; j++) { Hadamard(values, 32 + i * 4 + j, 35 + i * 4 - j, (i & 1) != 0); }
                }
            }
            for (int i = 0; i < 2; i++) { Rotate(values, 2 * i, 2 * i + 1, 32 + 16 * i, i == 0); }
            if (n >= 3)
            {
                for (int i = 0; i < 2; i++) { Hadamard(values, 4 + 2 * i, 5 + 2 * i, i != 0); }
            }
            if (n >= 4)
            {
                for (int i = 0; i < 2; i++) { Rotate(values, 14 - i, 9 + i, 48 + 64 * i, true); }
            }
            if (n >= 5)
            {
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 2; j++) { Hadamard(values, 16 + 4 * i + j, 19 + 4 * i - j, (i & 1) != 0); }
                }
            }
            if (n == 6)
            {
                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 4; j++) { Rotate(values, 61 - i * 8 - j, 34 + i * 8 + j, 56 - i * 32 + (j >> 1) * 64, true); }
                }
            }
            for (int i = 0; i < 2; i++) { Hadamard(values, i, 3 - i, false); }
            if (n >= 3) { Rotate(values, 6, 5, 32, true); }
            if (n >= 4)
            {
                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 2; j++) { Hadamard(values, 8 + 4 * i + j, 11 + 4 * i - j, i != 0); }
                }
            }
            if (n >= 5)
            {
                for (int i = 0; i < 4; i++) { Rotate(values, 29 - i, 18 + i, 48 + (i >> 1) * 64, true); }
            }
            if (n == 6)
            {
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++) { Hadamard(values, 32 + 8 * i + j, 39 + 8 * i - j, (i & 1) != 0); }
                }
            }
            if (n >= 3)
            {
                for (int i = 0; i < 4; i++) { Hadamard(values, i, 7 - i, false); }
            }
            if (n >= 4)
            {
                for (int i = 0; i < 2; i++) { Rotate(values, 13 - i, 10 + i, 32, true); }
            }
            if (n >= 5)
            {
                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 4; j++) { Hadamard(values, 16 + i * 8 + j, 23 + i * 8 - j, i != 0); }
                }
            }
            if (n == 6)
            {
                for (int i = 0; i < 8; i++) { Rotate(values, 59 - i, 36 + i, i < 4 ? 48 : 112, true); }
            }
            if (n >= 4)
            {
                for (int i = 0; i < 8; i++) { Hadamard(values, i, 15 - i, false); }
            }
            if (n >= 5)
            {
                for (int i = 0; i < 4; i++) { Rotate(values, 27 - i, 20 + i, 32, true); }
            }
            if (n == 6)
            {
                for (int i = 0; i < 8; i++)
                {
                    Hadamard(values, 32 + i, 47 - i, false);
                    Hadamard(values, 48 + i, 63 - i, true);
                }
            }
            if (n >= 5)
            {
                for (int i = 0; i < 16; i++) { Hadamard(values, i, 31 - i, false); }
            }
            if (n == 6)
            {
                for (int i = 0; i < 8; i++) { Rotate(values, 55 - i, 40 + i, 32, true); }
                for (int i = 0; i < 32; i++) { Hadamard(values, i, 63 - i, false); }
            }
        }
        private static void Dct4(Span<int> values)
        {
            int first = Round((long)(values[0] + values[2]) * 2896);
            int second = Round((long)(values[0] - values[2]) * 2896);
            int third = Round((long)values[1] * 1567 - (long)values[3] * 3784);
            int fourth = Round((long)values[1] * 3784 + (long)values[3] * 1567);
            Validate(first); Validate(second); Validate(third); Validate(fourth);
            values[0] = Clip(first + fourth);
            values[1] = Clip(second + third);
            values[2] = Clip(second - third);
            values[3] = Clip(first - fourth);
        }
    }
}
