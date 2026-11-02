// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacHuffmanEncoder
    {
        private static readonly uint[][] Codes = CreateCodes();

        public static int CountSymbol(int codebook, int symbol) => (int)(Codes[codebook][symbol] & 31);

        public static (byte Book, int Bits) SelectCodebook(ReadOnlySpan<int> values)
        {
            if ((values.Length & 3) != 0)
            {
                throw new ArgumentException("AAC spectral bands require a multiple of four coefficients.", nameof(values));
            }
            int maximum = 0;
            int signs = 0;
            foreach (int value in values)
            {
                maximum = Math.Max(maximum, Math.Abs(value));
                if (value != 0) { signs++; }
            }
            if (maximum == 0) { return (0, 0); }
            byte selected = 0;
            int best = int.MaxValue;
            for (int codebook = 1; codebook <= 9; codebook += 2)
            {
                AacSpectralCodebook book = AacSpectralCodebook.Get(codebook);
                if (maximum > book.MaximumSymbolMagnitude) { continue; }
                int first = book.Signed ? 0 : signs;
                int second = first;
                uint[] firstCodes = Codes[codebook];
                uint[] secondCodes = Codes[codebook + 1];
                for (int start = 0; start < values.Length; start += book.Dimensions)
                {
                    int symbol = book.Symbol(values.Slice(start, book.Dimensions));
                    first += (int)(firstCodes[symbol] & 31);
                    second += (int)(secondCodes[symbol] & 31);
                    if (first >= best && second >= best) { break; }
                }
                if (first < best) { best = first; selected = (byte)codebook; }
                if (second < best) { best = second; selected = (byte)(codebook + 1); }
            }
            int escape = Count(11, values);
            if (escape < best) { return (11, escape); }
            return (selected, best);
        }

        public static void WriteSymbol(ref AacBitWriter bits, int codebook, int symbol)
        {
            uint packed = Codes[codebook][symbol];
            bits.Write(packed >> 5, (int)(packed & 31));
        }

        public static int Count(int codebook, ReadOnlySpan<int> values)
        {
            AacSpectralCodebook book = AacSpectralCodebook.Get(codebook);
            int total = 0;
            for (int start = 0; start < values.Length; start += book.Dimensions)
            {
                ReadOnlySpan<int> tuple = values.Slice(start, book.Dimensions);
                int symbol = book.Symbol(tuple);
                if (symbol < 0)
                {
                    return int.MaxValue;
                }
                total += (int)(Codes[codebook][symbol] & 31);
                for (int index = 0; index < tuple.Length; index++)
                {
                    int magnitude = Math.Abs(tuple[index]);
                    if (!book.Signed && magnitude != 0)
                    {
                        total++;
                    }
                    if (codebook == 11 && magnitude >= 16)
                    {
                        total += 2 * BitOperations.Log2((uint)magnitude) - 3;
                    }
                }
            }
            return total;
        }

        public static void Write(ref AacBitWriter bits, int codebook, ReadOnlySpan<int> values)
        {
            AacSpectralCodebook book = AacSpectralCodebook.Get(codebook);
            for (int start = 0; start < values.Length; start += book.Dimensions)
            {
                ReadOnlySpan<int> tuple = values.Slice(start, book.Dimensions);
                int symbol = book.Symbol(tuple);
                if (symbol < 0)
                {
                    throw new InvalidOperationException("The spectral tuple cannot be represented by its AAC codebook.");
                }
                WriteSymbol(ref bits, codebook, symbol);
                if (!book.Signed)
                {
                    foreach (int value in tuple)
                    {
                        if (value != 0)
                        {
                            bits.Write(value < 0 ? 1u : 0, 1);
                        }
                    }
                }
                if (codebook == 11)
                {
                    foreach (int value in tuple)
                    {
                        int magnitude = Math.Abs(value);
                        if (magnitude >= 16)
                        {
                            int count = BitOperations.Log2((uint)magnitude);
                            bits.Write((1u << (count - 4)) - 1, count - 4);
                            bits.Write(0, 1);
                            bits.Write((uint)(magnitude - (1 << count)), count);
                        }
                    }
                }
            }
        }

        private static uint[][] CreateCodes()
        {
            uint[][] result = new uint[12][];
            for (int book = 0; book < result.Length; book++)
            {
                ReadOnlySpan<short> tree = AacHuffmanTables.Get(book);
                int count = 0;
                foreach (short node in tree)
                {
                    count = Math.Max(count, -node);
                }
                result[book] = new uint[count];
                Visit(tree, 0, 0, 0, result[book]);
            }
            return result;
        }

        private static void Visit(ReadOnlySpan<short> tree, int node, uint code, int length, uint[] output)
        {
            if (node < 0)
            {
                output[-node - 1] = (code << 5) | (uint)length;
                return;
            }
            Visit(tree, tree[node * 2], code << 1, length + 1, output);
            Visit(tree, tree[node * 2 + 1], (code << 1) | 1, length + 1, output);
        }
    }
}
