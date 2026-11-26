// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg2
{
    internal class Mpeg2Vlc
    {
        private readonly short[] edges;
        private readonly int[] values;

        public Mpeg2Vlc(ReadOnlySpan<Mpeg2Codeword> words)
        {
            int capacity = 1;
            foreach (Mpeg2Codeword word in words) { capacity += word.Length; }
            short[] tree = new short[2 * capacity];
            values = new int[words.Length];
            int nextNode = 1;
            for (int i = 0; i < words.Length; i++)
            {
                Mpeg2Codeword word = words[i];
                int node = 0;
                for (int bit = word.Length - 1; bit > 0; bit--)
                {
                    int edge = 2 * node + (word.Code >> bit & 1);
                    if (tree[edge] == 0) { tree[edge] = checked((short)nextNode++); }
                    if (tree[edge] < 0) { throw new InvalidOperationException("Overlapping MPEG-2 VLC prefixes."); }
                    node = tree[edge];
                }
                int leaf = 2 * node + (word.Code & 1);
                if (tree[leaf] != 0) { throw new InvalidOperationException("Overlapping MPEG-2 VLC entries."); }
                tree[leaf] = checked((short)~i);
                values[i] = word.Value;
            }
            edges = tree[..(nextNode * 2)];
        }

        public int Read(scoped ref Mpeg2BitReader bits)
        {
            int node = 0;
            do
            {
                node = edges[node * 2 + bits.Read(1)];
                if (node == 0) { throw new InvalidDataException("Invalid MPEG-2 variable-length code."); }
            }
            while (node >= 0);
            return values[~node];
        }
    }
}
