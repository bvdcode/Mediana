// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.ProRes
{
    internal static class ProResCoefficients
    {
        public static void Read(ReadOnlySpan<byte> encoded, Span<int> coefficients, int blocks)
        {
            coefficients.Clear();
            ProResBitReader bits = new(encoded);
            coefficients[0] = Signed(bits.Symbol(-1, 0, 5));
            int previousDifference = 3;
            for (int i = 1; i < blocks; i++)
            {
                int symbol = Math.Abs(previousDifference) switch
                {
                    0 => bits.Symbol(-1, 0, 0),
                    1 => bits.Symbol(-1, 0, 1),
                    2 => bits.Symbol(1, 2, 3),
                    _ => bits.Symbol(-1, 0, 3)
                };
                int difference = Signed(symbol) * (previousDifference < 0 ? -1 : 1);
                coefficients[i] = checked(coefficients[i - 1] + difference);
                previousDifference = difference;
            }
            int position = blocks;
            int previousRun = 4;
            int previousLevel = 1;
            while (!bits.EndOfData)
            {
                int run = previousRun switch
                {
                    <= 1 => bits.Symbol(2, 0, 1),
                    <= 3 => bits.Symbol(1, 0, 1),
                    4 => bits.Symbol(-1, 0, 0),
                    <= 8 => bits.Symbol(1, 1, 2),
                    <= 14 => bits.Symbol(-1, 0, 1),
                    _ => bits.Symbol(-1, 0, 2)
                };
                position = checked(position + run);
                if (position >= coefficients.Length)
                {
                    throw new InvalidDataException("The ProRes coefficient run exceeds its slice.");
                }
                int level = previousLevel switch
                {
                    0 => bits.Symbol(2, 0, 2),
                    1 => bits.Symbol(1, 0, 1),
                    2 => bits.Symbol(2, 0, 1),
                    3 => bits.Symbol(-1, 0, 0),
                    <= 7 => bits.Symbol(-1, 0, 1),
                    _ => bits.Symbol(-1, 0, 2)
                };
                coefficients[position++] = (level + 1) * (bits.Read(1) == 0 ? 1 : -1);
                previousRun = run;
                previousLevel = level;
            }
        }

        private static int Signed(int symbol) => (symbol >> 1) ^ -(symbol & 1);
    }
}
