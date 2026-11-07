// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static partial class Vp9Transform
    {
        private static void Dct4(Span<int> t)
        {
            Rotate(t, 0, 1, 11585, 11585, true);
            Rotate(t, 2, 3, 6270, 15137, false);
            Hadamard(t, 0, 3);
            Hadamard(t, 1, 2);
        }

        private static void Dct8(Span<int> t)
        {
            Dct4(t);
            Rotate(t, 4, 7, 3196, 16069, false);
            Rotate(t, 5, 6, 13623, 9102, false);
            Hadamard(t, 4, 5);
            Hadamard(t, 7, 6);
            Rotate(t, 6, 5, 11585, 11585, true);
            Hadamard(t, 0, 7);
            Hadamard(t, 1, 6);
            Hadamard(t, 2, 5);
            Hadamard(t, 3, 4);
        }

        private static void Dct16(Span<int> t)
        {
            Dct8(t);
            Rotate(t, 8, 15, 1606, 16305, false);
            Rotate(t, 9, 14, 12665, 10394, false);
            Rotate(t, 10, 13, 7723, 14449, false);
            Rotate(t, 11, 12, 15679, 4756, false);
            Hadamard(t, 8, 9);
            Hadamard(t, 11, 10);
            Hadamard(t, 12, 13);
            Hadamard(t, 15, 14);
            Rotate(t, 14, 9, 6270, 15137, true);
            Rotate(t, 10, 13, -15137, -6270, true);
            Hadamard(t, 8, 11);
            Hadamard(t, 15, 12);
            Hadamard(t, 9, 10);
            Hadamard(t, 14, 13);
            Rotate(t, 13, 10, 11585, 11585, true);
            Rotate(t, 12, 11, 11585, 11585, true);
            Hadamard(t, 0, 15);
            Hadamard(t, 1, 14);
            Hadamard(t, 2, 13);
            Hadamard(t, 3, 12);
            Hadamard(t, 4, 11);
            Hadamard(t, 5, 10);
            Hadamard(t, 6, 9);
            Hadamard(t, 7, 8);
        }

        private static void Dct32(Span<int> t)
        {
            Dct16(t);
            Rotate(t, 16, 31, 804, 16364, false);
            Rotate(t, 17, 30, 12140, 11003, false);
            Rotate(t, 18, 29, 7005, 14811, false);
            Rotate(t, 19, 28, 15426, 5520, false);
            Rotate(t, 20, 27, 3981, 15893, false);
            Rotate(t, 21, 26, 14053, 8423, false);
            Rotate(t, 22, 25, 9760, 13160, false);
            Rotate(t, 23, 24, 16207, 2404, false);
            Hadamard(t, 16, 17);
            Hadamard(t, 19, 18);
            Hadamard(t, 20, 21);
            Hadamard(t, 23, 22);
            Hadamard(t, 24, 25);
            Hadamard(t, 27, 26);
            Hadamard(t, 28, 29);
            Hadamard(t, 31, 30);
            Rotate(t, 30, 17, 3196, 16069, true);
            Rotate(t, 22, 25, -9102, -13623, true);
            Rotate(t, 26, 21, 13623, 9102, true);
            Rotate(t, 18, 29, -16069, -3196, true);
            Hadamard(t, 16, 19);
            Hadamard(t, 23, 20);
            Hadamard(t, 24, 27);
            Hadamard(t, 31, 28);
            Hadamard(t, 17, 18);
            Hadamard(t, 22, 21);
            Hadamard(t, 25, 26);
            Hadamard(t, 30, 29);
            Rotate(t, 29, 18, 6270, 15137, true);
            Rotate(t, 21, 26, -15137, -6270, true);
            Rotate(t, 28, 19, 6270, 15137, true);
            Rotate(t, 20, 27, -15137, -6270, true);
            Hadamard(t, 16, 23);
            Hadamard(t, 31, 24);
            Hadamard(t, 17, 22);
            Hadamard(t, 30, 25);
            Hadamard(t, 18, 21);
            Hadamard(t, 29, 26);
            Hadamard(t, 19, 20);
            Hadamard(t, 28, 27);
            Rotate(t, 27, 20, 11585, 11585, true);
            Rotate(t, 26, 21, 11585, 11585, true);
            Rotate(t, 25, 22, 11585, 11585, true);
            Rotate(t, 24, 23, 11585, 11585, true);
            Hadamard(t, 0, 31);
            Hadamard(t, 1, 30);
            Hadamard(t, 2, 29);
            Hadamard(t, 3, 28);
            Hadamard(t, 4, 27);
            Hadamard(t, 5, 26);
            Hadamard(t, 6, 25);
            Hadamard(t, 7, 24);
            Hadamard(t, 8, 23);
            Hadamard(t, 9, 22);
            Hadamard(t, 10, 21);
            Hadamard(t, 11, 20);
            Hadamard(t, 12, 19);
            Hadamard(t, 13, 18);
            Hadamard(t, 14, 17);
            Hadamard(t, 15, 16);
        }

    }
}
