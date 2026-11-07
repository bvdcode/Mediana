// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static partial class Vp9Transform
    {
        private static void Adst4(Span<int> t)
        {
            long x0 = 5283L * t[0] + 15212L * t[2] + 9929L * t[3];
            long x1 = 9929L * t[0] - 5283L * t[2] - 15212L * t[3];
            long x2 = 13377L * (t[0] - t[2] + t[3]), x3 = 13377L * t[1];
            t[0] = Round(x0 + x3); t[1] = Round(x1 + x3); t[2] = Round(x2); t[3] = Round(x0 + x1 - x3);
        }

        private static void Adst8(Span<int> t)
        {
            Span<long> s = stackalloc long[8];
            for (int i = 0; i < 4; i++) { WideRotate(t, s, 2 * i, 1 + 2 * i, 30 - 8 * i); }
            for (int i = 0; i < 4; i++) { WideHadamard(t, s, i, 4 + i); }
            for (int i = 0; i < 2; i++) { WideRotate(t, s, 4 + 3 * i, 5 + i, 24 - 16 * i); }
            for (int i = 0; i < 2; i++) { WideHadamard(t, s, 4 + i, 6 + i); Hadamard(t, i, 2 + i); }
            for (int i = 0; i < 2; i++) { Rotate(t, 2 + 4 * i, 3 + 4 * i, 11585, 11585, true); }
            Span<int> copy = stackalloc int[8]; t.CopyTo(copy);
            ReadOnlySpan<byte> order = [0, 4, 6, 2, 3, 7, 5, 1];
            for (int i = 0; i < 8; i++) { t[i] = (i & 1) == 0 ? copy[order[i]] : -copy[order[i]]; }
        }

        private static void Adst16(Span<int> t)
        {
            Span<long> s = stackalloc long[16];
            for (int i = 0; i < 8; i++) { WideRotate(t, s, 2 * i, 1 + 2 * i, 31 - 4 * i); }
            for (int i = 0; i < 8; i++) { WideHadamard(t, s, i, 8 + i); }
            for (int i = 0; i < 4; i++) { WideRotate(t, s, 8 + 2 * i, 9 + 2 * i, 28 - 16 * i); }
            for (int i = 0; i < 4; i++) { WideHadamard(t, s, 8 + i, 12 + i); Hadamard(t, i, 4 + i); }
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++) { WideRotate(t, s, 4 + 8 * i + 3 * j, 5 + 8 * i + j, 24 - 16 * j); }
            }
            for (int j = 0; j < 2; j++)
            {
                for (int i = 0; i < 2; i++)
                {
                    WideHadamard(t, s, 4 + 8 * j + i, 6 + 8 * j + i); Hadamard(t, 8 * j + i, 2 + 8 * j + i);
                }
            }
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++) { Rotate(t, 2 + 4 * j + 8 * i, 3 + 4 * j + 8 * i, (i ^ j) == 0 ? -11585 : 11585, (i ^ j) == 0 ? 11585 : -11585, false); }
            }
            Span<int> copy = stackalloc int[16]; t.CopyTo(copy);
            ReadOnlySpan<byte> order = [0, 8, 12, 4, 6, 14, 10, 2, 3, 11, 15, 7, 5, 13, 9, 1];
            for (int i = 0; i < 16; i++) { t[i] = i is 1 or 3 or 13 or 15 ? -copy[order[i]] : copy[order[i]]; }
        }

        private static void WideRotate(ReadOnlySpan<int> t, Span<long> s, int a, int b, int angle)
        {
            int cosine = Cosine(angle), sine = Cosine(angle - 32);
            s[b] = (long)t[a] * cosine - (long)t[b] * sine;
            s[a] = (long)t[a] * sine + (long)t[b] * cosine;
        }

        private static int Cosine(int angle)
        {
            ReadOnlySpan<short> cosine = [16384, 16364, 16305, 16207, 16069, 15893, 15679, 15426, 15137, 14811, 14449, 14053,
                13623, 13160, 12665, 12140, 11585, 11003, 10394, 9760, 9102, 8423, 7723, 7005, 6270, 5520, 4756, 3981, 3196, 2404, 1606, 804, 0];
            int quadrant = angle & 127;
            if (quadrant <= 32) { return cosine[quadrant]; }
            if (quadrant <= 64) { return -cosine[64 - quadrant]; }
            if (quadrant <= 96) { return -cosine[quadrant - 64]; }
            return cosine[128 - quadrant];
        }

        private static void WideHadamard(Span<int> t, ReadOnlySpan<long> s, int a, int b)
        { t[a] = Round(s[a] + s[b]); t[b] = Round(s[a] - s[b]); }
    }
}
