// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal class WebpHuffmanCode
    {
        private static ReadOnlySpan<int> LengthOrder => [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        private readonly byte[] lengths;
        private readonly uint[] codes;
        private readonly int[] symbols;

        internal WebpHuffmanCode(ReadOnlySpan<int> histogram, int maximumBits = 15)
        {
            lengths = new byte[histogram.Length];
            codes = new uint[histogram.Length];
            List<int> used = [];
            for (int symbol = 0; symbol < histogram.Length; symbol++)
            {
                if (histogram[symbol] != 0) { used.Add(symbol); }
            }
            if (used.Count == 0) { used.Add(0); }
            symbols = used.ToArray();
            if (symbols.Length == 1) { lengths[symbols[0]] = 1; return; }
            int[] weights = histogram.ToArray();
            while (true)
            {
                PriorityQueue<int, (long Weight, int Symbol)> queue = new(symbols.Length);
                int[] parents = new int[histogram.Length * 2];
                Array.Fill(parents, -1);
                foreach (int symbol in symbols) { queue.Enqueue(symbol, (weights[symbol], symbol)); }
                int node = histogram.Length;
                while (queue.Count > 1)
                {
                    queue.TryDequeue(out int first, out (long Weight, int Symbol) a);
                    queue.TryDequeue(out int second, out (long Weight, int Symbol) b);
                    parents[first] = node;
                    parents[second] = node;
                    queue.Enqueue(node++, (a.Weight + b.Weight, Math.Min(a.Symbol, b.Symbol)));
                }
                int deepest = 0;
                foreach (int symbol in symbols)
                {
                    int depth = 0;
                    for (int parent = parents[symbol]; parent >= 0; parent = parents[parent]) { depth++; }
                    lengths[symbol] = (byte)depth;
                    deepest = Math.Max(deepest, depth);
                }
                if (deepest <= maximumBits) { break; }
                foreach (int symbol in symbols) { weights[symbol] = (weights[symbol] + 1) / 2; }
            }
            Span<int> counts = stackalloc int[16];
            Span<uint> next = stackalloc uint[16];
            counts.Clear();
            next.Clear();
            foreach (int symbol in symbols) { counts[lengths[symbol]]++; }
            uint code = 0;
            for (int bits = 1; bits <= maximumBits; bits++)
            {
                code = (code + (uint)counts[bits - 1]) << 1;
                next[bits] = code;
            }
            foreach (int symbol in symbols) { codes[symbol] = Reverse(next[lengths[symbol]]++, lengths[symbol]); }
        }

        internal long DataBits(ReadOnlySpan<int> histogram)
        {
            if (symbols.Length == 1) { return 0; }
            long bits = 0;
            foreach (int symbol in symbols) { bits += (long)histogram[symbol] * lengths[symbol]; }
            return bits;
        }

        internal void WriteSymbol(WebpBitWriter output, int symbol) => output.Write(codes[symbol], symbols.Length == 1 ? 0 : lengths[symbol]);

        internal void WriteHeader(WebpBitWriter output)
        {
            if (symbols.Length <= 2 && symbols[^1] < 256)
            {
                output.Write(1, 1);
                output.Write((uint)(symbols.Length - 1), 1);
                int firstBits = symbols[0] < 2 ? 1 : 8;
                output.Write(firstBits == 8 ? 1u : 0, 1);
                output.Write((uint)symbols[0], firstBits);
                if (symbols.Length == 2) { output.Write((uint)symbols[1], 8); }
                return;
            }
            List<WebpCodeLength> tokens = EncodeLengths();
            Span<int> histogram = stackalloc int[19];
            histogram.Clear();
            foreach (WebpCodeLength token in tokens) { histogram[token.Symbol]++; }
            WebpHuffmanCode tree = new(histogram, 7);
            int count = 19;
            while (count > 4 && tree.lengths[LengthOrder[count - 1]] == 0) { count--; }
            output.Write(0, 1);
            output.Write((uint)(count - 4), 4);
            for (int index = 0; index < count; index++) { output.Write(tree.lengths[LengthOrder[index]], 3); }
            output.Write(0, 1);
            foreach (WebpCodeLength token in tokens)
            {
                tree.WriteSymbol(output, token.Symbol);
                output.Write(token.Extra, token.ExtraBits);
            }
        }

        private List<WebpCodeLength> EncodeLengths()
        {
            List<WebpCodeLength> tokens = [];
            for (int index = 0; index < lengths.Length;)
            {
                byte length = lengths[index];
                int end = index + 1;
                while (end < lengths.Length && lengths[end] == length) { end++; }
                int run = end - index;
                if (length != 0)
                {
                    tokens.Add(new(length));
                    run--;
                    while (run >= 3)
                    {
                        int count = Math.Min(run, 6);
                        tokens.Add(new(16, (uint)(count - 3), 2));
                        run -= count;
                    }
                }
                else
                {
                    while (run >= 11)
                    {
                        int count = Math.Min(run, 138);
                        tokens.Add(new(18, (uint)(count - 11), 7));
                        run -= count;
                    }
                    if (run >= 3)
                    {
                        tokens.Add(new(17, (uint)(run - 3), 3));
                        run = 0;
                    }
                }
                while (run-- > 0) { tokens.Add(new(length)); }
                index = end;
            }
            return tokens;
        }

        private static uint Reverse(uint value, int count)
        {
            value = ((value & 0x55555555) << 1) | ((value >> 1) & 0x55555555);
            value = ((value & 0x33333333) << 2) | ((value >> 2) & 0x33333333);
            value = ((value & 0x0f0f0f0f) << 4) | ((value >> 4) & 0x0f0f0f0f);
            value = ((value & 0x00ff00ff) << 8) | ((value >> 8) & 0x00ff00ff);
            value = (value << 16) | (value >> 16);
            return value >> (32 - count);
        }
    }
}
