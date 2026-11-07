// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal class TheoraHuffmanTable
    {
        private readonly short[] children = new short[64];
        private short root;
        private int branches, leaves;

        internal static TheoraHuffmanTable Read(ref TheoraBitReader reader)
        {
            TheoraHuffmanTable result = new(); result.root = result.ReadNode(ref reader, 0); return result;
        }

        private short ReadNode(ref TheoraBitReader reader, int depth)
        {
            if (depth > 32) { throw new InvalidDataException("A Theora Huffman code exceeds 32 bits."); }
            if (reader.Flag())
            {
                if (++leaves > 32) { throw new InvalidDataException("A Theora Huffman table exceeds 32 entries."); }
                return (short)(-reader.Read(5) - 1);
            }
            if (branches == 31) { throw new InvalidDataException("A Theora Huffman tree exceeds 32 entries."); }
            short node = (short)branches++;
            children[node * 2] = ReadNode(ref reader, depth + 1);
            children[node * 2 + 1] = ReadNode(ref reader, depth + 1);
            return node;
        }

        internal int Decode(ref TheoraBitReader reader)
        {
            int node = root;
            while (node >= 0) { node = children[node * 2 + reader.Read(1)]; }
            return -node - 1;
        }
    }
}
