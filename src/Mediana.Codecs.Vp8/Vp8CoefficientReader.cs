// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal static class Vp8CoefficientReader
    {
        private static ReadOnlySpan<sbyte> Tree => [-11, 2, 0, 4, -1, 6, 8, 12, -2, 10, -3, -4, 14, 16, -5, -6, 18, 20, -7, -8, -9, -10];
        private static ReadOnlySpan<byte> Extras => [159, 165, 145, 173, 148, 140, 176, 155, 140, 135, 180, 157, 141, 134, 130, 254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129];
        internal static bool Read(Vp8BooleanReader input, ReadOnlySpan<byte> probabilities, Span<int> output, int type, int context, int dc, int ac)
            => ReadNonzeroEnd(input, probabilities, output, type, context, dc, ac) != 0;

        internal static int ReadNonzeroEnd(Vp8BooleanReader input, ReadOnlySpan<byte> probabilities, Span<int> output, int type, int context, int dc, int ac)
        {
            bool zero = false;
            int nonzeroEnd = 0;
            for (int position = type == 0 ? 1 : 0; position < 16; position++)
            {
                ReadOnlySpan<byte> model = probabilities.Slice(((type * 8 + Vp8ProbabilityTables.Bands[position]) * 3 + context) * 11, 11);
                int token = input.Tree(Tree, model, zero ? 2 : 0);
                if (token == 11) { break; }
                int value = token;
                if (token >= 5)
                {
                    int count = token == 10 ? 11 : token - 4, start = token == 10 ? 15 : count * (count - 1) / 2;
                    int extra = 0;
                    for (int index = 0; index < count; index++) { extra = (extra << 1) | (input.Read(Extras[start + index]) ? 1 : 0); }
                    value = (token == 10 ? 67 : (1 << count) + 3) + extra;
                }
                zero = value == 0; context = Math.Min(value, 2); if (!zero) { nonzeroEnd = position + 1; }
                if (!zero && input.Read()) { value = -value; }
                output[Vp8ProbabilityTables.Zigzag[position]] = value * (position == 0 ? dc : ac);
            }
            return nonzeroEnd;
        }
        internal static void Walsh(ReadOnlySpan<int> input, Span<int> output)
        {
            Span<int> temporary = stackalloc int[16];
            for (int x = 0; x < 4; x++)
            {
                int a = input[x] + input[x + 12], b = input[x + 4] + input[x + 8], c = input[x + 4] - input[x + 8], d = input[x] - input[x + 12];
                temporary[x] = a + b; temporary[x + 4] = c + d; temporary[x + 8] = a - b; temporary[x + 12] = d - c;
            }
            for (int y = 0; y < 4; y++)
            {
                int offset = y * 4, a = temporary[offset] + temporary[offset + 3], b = temporary[offset + 1] + temporary[offset + 2];
                int c = temporary[offset + 1] - temporary[offset + 2], d = temporary[offset] - temporary[offset + 3];
                output[offset] = (a + b + 3) >> 3; output[offset + 1] = (c + d + 3) >> 3;
                output[offset + 2] = (a - b + 3) >> 3; output[offset + 3] = (d - c + 3) >> 3;
            }
        }
    }
}
