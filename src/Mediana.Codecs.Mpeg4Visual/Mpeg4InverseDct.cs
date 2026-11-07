// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg4Visual
{
    internal static class Mpeg4InverseDct
    {
        internal static void Write(ReadOnlySpan<int> coefficients, Span<byte> target, int stride, bool intra)
        {
            bool dcOnly = true;
            for (int i = 1; i < 64; i++) { if (coefficients[i] != 0) { dcOnly = false; break; } }
            if (dcOnly)
            {
                int dc = (coefficients[0] + 4) >> 3;
                if (intra)
                { for (int y = 0; y < 8; y++) { target.Slice(y * stride, 8).Fill((byte)Math.Clamp(dc, 0, 255)); } }
                else if (dc != 0)
                { for (int y = 0; y < 8; y++) { for (int x = 0; x < 8; x++) { int at = y * stride + x; target[at] = (byte)Math.Clamp(target[at] + dc, 0, 255); } } }
                return;
            }
            Span<long> rows = stackalloc long[64], input = stackalloc long[8], column = stackalloc long[8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++) { input[x] = coefficients[y * 8 + x]; }
                Transform(input, rows.Slice(y * 8, 8));
            }
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++) { input[y] = rows[y * 8 + x]; }
                Transform(input, column);
                for (int y = 0; y < 8; y++)
                {
                    int at = y * stride + x, value = (int)((column[y] + (1L << 29)) >> 30);
                    if (!intra) { value += target[at]; } target[at] = (byte)Math.Clamp(value, 0, 255);
                }
            }
        }
        private static void Transform(ReadOnlySpan<long> c, Span<long> output)
        {
            long a = (c[0] + c[4]) * 11585, b = (c[0] - c[4]) * 11585;
            long d = c[2] * 15137 + c[6] * 6270, e = c[2] * 6270 - c[6] * 15137;
            long o0 = c[1] * 16069 + c[3] * 13623 + c[5] * 9102 + c[7] * 3196;
            long o1 = c[1] * 13623 - c[3] * 3196 - c[5] * 16069 - c[7] * 9102;
            long o2 = c[1] * 9102 - c[3] * 16069 + c[5] * 3196 + c[7] * 13623;
            long o3 = c[1] * 3196 - c[3] * 9102 + c[5] * 13623 - c[7] * 16069;
            output[0] = a + d + o0; output[7] = a + d - o0;
            output[1] = b + e + o1; output[6] = b + e - o1;
            output[2] = b - e + o2; output[5] = b - e - o2;
            output[3] = a - d + o3; output[4] = a - d - o3;
        }
    }
}
