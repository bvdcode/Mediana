// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacHuffman
    {
        private const int PrefixBits = 8;
        private const int PrefixCount = 1 << PrefixBits;
        private static readonly int[] Prefixes = CreatePrefixes();

        public static int ReadScaleFactor(ref AacBitReader bits) => ReadSymbol(ref bits, 0) - 60;

        public static int ReadSpectrum(ref AacBitReader bits, int codebook, scoped Span<int> values)
        {
            AacSpectralCodebook book = AacSpectralCodebook.Get(codebook);
            int dimensions = book.Dimensions;
            if (values.Length < dimensions)
            {
                throw new ArgumentException("The spectral tuple buffer is too small.", nameof(values));
            }
            int symbol = ReadSymbol(ref bits, codebook);
            book.Read(symbol, values);
            if (!book.Signed)
            {
                for (int index = 0; index < dimensions; index++)
                {
                    if (values[index] != 0 && bits.Read(1) != 0)
                    {
                        values[index] = -values[index];
                    }
                }
            }
            if (codebook == 11)
            {
                for (int index = 0; index < dimensions; index++)
                {
                    if (Math.Abs(values[index]) == 16)
                    {
                        int count = 4;
                        while (bits.Read(1) != 0)
                        {
                            if (++count > 12)
                            {
                                throw new InvalidDataException("The AAC spectral escape exceeds its allowed magnitude.");
                            }
                        }
                        int magnitude = (1 << count) | bits.Read(count);
                        values[index] = values[index] < 0 ? -magnitude : magnitude;
                    }
                }
            }
            return dimensions;
        }

        internal static int ReadSymbol(ref AacBitReader bits, int codebook)
        {
            ReadOnlySpan<short> tree = AacHuffmanTables.Get(codebook);
            int entry = Prefixes[codebook * PrefixCount + bits.PeekByte()];
            bits.Skip(entry & 15);
            int node = entry >> 4;
            if (node < 0)
            {
                return -node - 1;
            }
            for (int length = PrefixBits; length < 19; length++)
            {
                node = tree[node * 2 + bits.Read(1)];
                if (node < 0)
                {
                    return -node - 1;
                }
            }
            throw new InvalidDataException("The AAC Huffman codeword is invalid.");
        }

        private static int[] CreatePrefixes()
        {
            int[] result = new int[12 * PrefixCount];
            for (int codebook = 0; codebook < 12; codebook++)
            {
                ReadOnlySpan<short> tree = AacHuffmanTables.Get(codebook);
                for (int prefix = 0; prefix < PrefixCount; prefix++)
                {
                    int node = 0;
                    int length = 0;
                    while (length < PrefixBits && node >= 0)
                    {
                        node = tree[node * 2 + ((prefix >> (PrefixBits - 1 - length)) & 1)];
                        length++;
                    }
                    result[codebook * PrefixCount + prefix] = (node << 4) | length;
                }
            }
            return result;
        }
    }
}
