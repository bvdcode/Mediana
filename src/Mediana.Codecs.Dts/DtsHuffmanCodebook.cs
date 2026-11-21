// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    internal class DtsHuffmanCodebook
    {
        private readonly int[] branches;
        private readonly int[] symbols;

        public DtsHuffmanCodebook(ReadOnlySpan<int> triples)
        {
            int count = triples.Length / 3;
            branches = new int[count * 4];
            symbols = new int[count * 2];
            Array.Fill(branches, -1);
            Array.Fill(symbols, int.MinValue);
            int nodes = 1;
            for (int entry = 0; entry < triples.Length; entry += 3)
            {
                int node = 0, length = triples[entry + 1], code = triples[entry + 2];
                for (int bit = length - 1; bit >= 0; bit--)
                {
                    int branch = node * 2 + ((code >> bit) & 1);
                    if (branches[branch] < 0) { branches[branch] = nodes++; }
                    node = branches[branch];
                }
                symbols[node] = triples[entry];
            }
        }

        public int Read(ref DtsBitReader bits)
        {
            int node = 0;
            while (symbols[node] == int.MinValue)
            {
                node = branches[node * 2 + bits.Read(1)];
                if (node < 0) { throw new InvalidDataException("The DTS Huffman code is invalid."); }
            }
            return symbols[node];
        }
    }
}
