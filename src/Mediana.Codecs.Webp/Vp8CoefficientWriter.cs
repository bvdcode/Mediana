// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Vp8;

namespace Mediana.Codecs.Webp
{
    internal static class Vp8CoefficientWriter
    {
        private static ReadOnlySpan<byte> Codes => [2, 6, 28, 58, 59, 60, 61, 124, 125, 126, 127, 0];
        private static ReadOnlySpan<byte> Lengths => [2, 3, 5, 6, 6, 6, 6, 7, 7, 7, 7, 1];
        private static ReadOnlySpan<sbyte> Tree => [-11, 2, 0, 4, -1, 6, 8, 12, -2, 10, -3, -4, 14, 16, -5, -6, 18, 20, -7, -8, -9, -10];
        private static ReadOnlySpan<byte> ExtraProbabilities => [159, 165, 145, 173, 148, 140, 176, 155, 140, 135, 180, 157, 141, 134, 130, 254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129];

        internal static bool Write(Vp8BooleanWriter writer, ReadOnlySpan<short> coefficients, int plane, int context)
        {
            int last = 15;
            while (last >= 0 && coefficients[Vp8ProbabilityTables.Zigzag[last]] == 0) { last--; }
            bool precedingZero = false;
            for (int position = 0; position < 16; position++)
            {
                ReadOnlySpan<byte> probabilities = Vp8ProbabilityTables.Coefficients.Slice(
                    ((plane * 8 + Vp8ProbabilityTables.Bands[position]) * 3 + context) * 11, 11);
                if (position > last) { WriteToken(writer, 11, probabilities, false); break; }
                int value = coefficients[Vp8ProbabilityTables.Zigzag[position]];
                int magnitude = Math.Abs(value);
                int category = magnitude switch
                {
                    <= 4 => magnitude,
                    <= 6 => 5,
                    <= 10 => 6,
                    <= 18 => 7,
                    <= 34 => 8,
                    <= 66 => 9,
                    <= 2114 => 10,
                    _ => throw new InvalidOperationException("VP8 coefficient exceeds its token range.")
                };
                WriteToken(writer, category, probabilities, precedingZero);
                if (category >= 5)
                {
                    int extraCount = category == 10 ? 11 : category - 4;
                    int minimum = category == 10 ? 67 : (1 << extraCount) + 3;
                    int extra = magnitude - minimum;
                    int start = category == 10 ? 15 : (extraCount * (extraCount - 1)) / 2;
                    for (int index = 0; index < extraCount; index++)
                    {
                        writer.Write(((extra >> (extraCount - 1 - index)) & 1) != 0, ExtraProbabilities[start + index]);
                    }
                }
                if (value != 0) { writer.Write(value < 0); }
                precedingZero = value == 0;
                context = Math.Min(magnitude, 2);
            }
            return last >= 0;
        }

        private static void WriteToken(Vp8BooleanWriter writer, int token, ReadOnlySpan<byte> probabilities, bool skipEnd)
        {
            int node = skipEnd ? 2 : 0;
            int bits = Lengths[token] - (skipEnd ? 1 : 0);
            for (int bit = bits - 1; bit >= 0; bit--)
            {
                bool value = ((Codes[token] >> bit) & 1) != 0;
                writer.Write(value, probabilities[node / 2]);
                node = Tree[node + (value ? 1 : 0)];
            }
        }
    }
}
