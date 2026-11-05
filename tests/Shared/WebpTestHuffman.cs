// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class WebpTestHuffman
    {
        private readonly Dictionary<(int Bits, int Code), int> table = [];
        private readonly int? only;
        private readonly int maximum;

        private WebpTestHuffman(int[] lengths)
        {
            int[] symbols = Enumerable.Range(0, lengths.Length).Where(symbol => lengths[symbol] != 0).ToArray();
            if (symbols.Length == 1) { only = symbols[0]; return; }
            maximum = lengths.Max();
            int code = 0;
            for (int bits = 1; bits <= maximum; bits++)
            {
                foreach (int symbol in symbols.Where(symbol => lengths[symbol] == bits)) { table.Add((bits, code++), symbol); }
                code <<= 1;
            }
        }

        internal int Decode(WebpTestBitReader reader)
        {
            if (only is int symbol) { return symbol; }
            int code = 0;
            for (int bits = 1; bits <= maximum; bits++)
            {
                code = (code << 1) | reader.Read(1);
                if (table.TryGetValue((bits, code), out int value)) { return value; }
            }
            throw new InvalidDataException("Invalid WebP reference prefix code.");
        }

        internal static WebpTestHuffman Read(WebpTestBitReader reader, int alphabet)
        {
            int[] lengths = new int[alphabet];
            if (reader.Read(1) != 0)
            {
                int count = reader.Read(1) + 1;
                int bits = reader.Read(1) == 0 ? 1 : 8;
                lengths[reader.Read(bits)] = 1;
                if (count == 2) { lengths[reader.Read(8)] = 1; }
            }
            else
            {
                int count = reader.Read(4) + 4;
                int[] order = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
                int[] nested = new int[19];
                for (int index = 0; index < count; index++) { nested[order[index]] = reader.Read(3); }
                WebpTestHuffman tree = new(nested);
                int remaining = alphabet;
                if (reader.Read(1) != 0) { remaining = reader.Read(2 + 2 * reader.Read(3)) + 2; }
                int position = 0;
                int previous = 8;
                while (position < alphabet && remaining-- > 0)
                {
                    int symbol = tree.Decode(reader);
                    if (symbol <= 15)
                    {
                        lengths[position++] = symbol;
                        if (symbol != 0) { previous = symbol; }
                    }
                    else
                    {
                        int repeat = symbol switch
                        {
                            16 => reader.Read(2) + 3,
                            17 => reader.Read(3) + 3,
                            18 => reader.Read(7) + 11,
                            _ => throw new InvalidDataException("Invalid WebP reference code-length repeat.")
                        };
                        for (int index = 0; index < repeat; index++) { lengths[position++] = symbol == 16 ? previous : 0; }
                    }
                }
            }
            return new(lengths);
        }
    }
}
