// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3HuffmanEncoder
    {
        private static readonly uint[][] Codes = new uint[32][];
        private static readonly byte[][] Lengths = new byte[32][];
        private static ReadOnlySpan<byte> Tables => [1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31];
        private static ReadOnlySpan<byte> Linbits => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 6, 8, 10, 13, 4, 5, 6, 7, 8, 9, 11, 13];

        static Mp3HuffmanEncoder()
        {
            foreach (int table in Tables)
            {
                Codes[table] = new uint[256];
                Lengths[table] = new byte[256];
                Walk(Mp3HuffmanTables.Get(table), 0, 0, 0, Codes[table], Lengths[table]);
            }
        }

        public static int Select(ReadOnlySpan<int> values, out int selected)
        {
            Span<int> histogram = stackalloc int[256];
            Span<byte> symbols = stackalloc byte[256];
            histogram.Clear();
            int count = 0;
            int signs = 0;
            int escapes = 0;
            int maximum = 0;
            for (int index = 0; index < values.Length; index += 2)
            {
                int x = Math.Abs(values[index]);
                int y = Math.Abs(values[index + 1]);
                maximum = Math.Max(maximum, Math.Max(x, y));
                signs += (x == 0 ? 0 : 1) + (y == 0 ? 0 : 1);
                escapes += (x >= 15 ? 1 : 0) + (y >= 15 ? 1 : 0);
                int symbol = Math.Min(x, 15) * 16 + Math.Min(y, 15);
                if (histogram[symbol]++ == 0) { symbols[count++] = (byte)symbol; }
            }
            if (maximum == 0)
            {
                selected = 0;
                return 0;
            }
            int best = int.MaxValue;
            selected = -1;
            foreach (int table in Tables)
            {
                int extension = Linbits[table];
                ReadOnlySpan<byte> lengths = Lengths[table];
                int bound = Math.Min(maximum, 15);
                if (lengths[bound * 17] == 0 || (maximum > 15 && (extension == 0 || maximum > 14 + (1 << extension)))) { continue; }
                int bits = signs + escapes * extension;
                for (int index = 0; index < count; index++)
                {
                    int symbol = symbols[index];
                    bits += lengths[symbol] * histogram[symbol];
                }
                if (bits < best)
                {
                    best = bits;
                    selected = table;
                }
            }
            return best;
        }

        public static void Write(ref Mp3BitWriter writer, ReadOnlySpan<int> values, int table)
        {
            if (table == 0) { return; }
            int extension = Linbits[table];
            for (int index = 0; index < values.Length; index += 2)
            {
                int x = Math.Abs(values[index]);
                int y = Math.Abs(values[index + 1]);
                int symbol = Math.Min(x, 15) * 16 + Math.Min(y, 15);
                writer.Write(Codes[table][symbol], Lengths[table][symbol]);
                if (x >= 15 && extension != 0) { writer.Write((uint)(x - 15), extension); }
                if (x != 0) { writer.Write(values[index] < 0 ? 1u : 0u, 1); }
                if (y >= 15 && extension != 0) { writer.Write((uint)(y - 15), extension); }
                if (y != 0) { writer.Write(values[index + 1] < 0 ? 1u : 0u, 1); }
            }
        }

        private static void Walk(ReadOnlySpan<short> tree, int node, uint code, int length, uint[] codes, byte[] lengths)
        {
            if (node < 0)
            {
                int symbol = -node - 1;
                codes[symbol] = code;
                lengths[symbol] = checked((byte)length);
                return;
            }
            Walk(tree, tree[node * 2], code << 1, length + 1, codes, lengths);
            Walk(tree, tree[node * 2 + 1], (code << 1) | 1, length + 1, codes, lengths);
        }
    }
}
