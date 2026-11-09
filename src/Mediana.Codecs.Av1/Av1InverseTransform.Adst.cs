// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1InverseTransform
    {
        private static void Adst(Span<int> values, int maximum)
        {
            if (values.Length == 4)
            {
                Adst4(values, maximum);
                return;
            }
            int count = values.Length;
            Span<int> copy = stackalloc int[16];
            values.CopyTo(copy);
            for (int i = 0; i < count; i++)
            {
                int index = (i & 1) != 0 ? i - 1 : count - i - 1;
                values[i] = copy[index];
            }
            if (count == 8)
            {
                for (int i = 0; i < 4; i++) { Rotate(values, 2 * i, 2 * i + 1, 60 - 16 * i, true, maximum); }
                for (int i = 0; i < 4; i++) { Hadamard(values, i, 4 + i, false, maximum); }
                for (int i = 0; i < 2; i++) { Rotate(values, 4 + 3 * i, 5 + i, 48 - 32 * i, true, maximum); }
            }
            else
            {
                for (int i = 0; i < 8; i++) { Rotate(values, 2 * i, 2 * i + 1, 62 - 8 * i, true, maximum); }
                for (int i = 0; i < 8; i++) { Hadamard(values, i, 8 + i, false, maximum); }
                for (int i = 0; i < 2; i++)
                {
                    Rotate(values, 8 + 2 * i, 9 + 2 * i, 56 - 32 * i, true, maximum);
                    Rotate(values, 13 + 2 * i, 12 + 2 * i, 8 + 32 * i, true, maximum);
                }
                for (int j = 0; j < 2; j++)
                {
                    for (int i = 0; i < 4; i++) { Hadamard(values, 8 * j + i, 4 + 8 * j + i, false, maximum); }
                }
                for (int j = 0; j < 2; j++)
                {
                    for (int i = 0; i < 2; i++) { Rotate(values, 4 + 8 * j + 3 * i, 5 + 8 * j + i, 48 - 32 * i, true, maximum); }
                }
            }
            for (int j = 0; j < count / 4; j++)
            {
                for (int i = 0; i < 2; i++) { Hadamard(values, 4 * j + i, 2 + 4 * j + i, false, maximum); }
            }
            for (int i = 0; i < count / 4; i++) { Rotate(values, 2 + 4 * i, 3 + 4 * i, 32, true, maximum); }
            values.CopyTo(copy);
            int shift = count == 8 ? 1 : 0;
            for (int i = 0; i < count; i++)
            {
                int a = (i >> 3) & 1;
                int b = ((i >> 2) & 1) ^ ((i >> 3) & 1);
                int c = ((i >> 1) & 1) ^ ((i >> 2) & 1);
                int d = (i & 1) ^ ((i >> 1) & 1);
                int index = ((d << 3) | (c << 2) | (b << 1) | a) >> shift;
                values[i] = (i & 1) != 0 ? -copy[index] : copy[index];
            }
        }

        private static void Adst4(Span<int> values, int maximum)
        {
            long a = (long)values[0] * 1321 + (long)values[2] * 3803 + (long)values[3] * 2482;
            long b = (long)values[0] * 2482 - (long)values[2] * 1321 - (long)values[3] * 3803;
            long c = (long)values[1] * 3344;
            int combined = values[0] - values[2] + values[3];
            Validate(combined, maximum);
            values[0] = Round(a + c);
            values[1] = Round(b + c);
            values[2] = Round((long)combined * 3344);
            values[3] = Round(a + b - c);
            for (int index = 0; index < 4; index++) { Validate(values[index], maximum); }
        }
    }
}
