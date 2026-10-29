namespace Mediana.Codecs.Aac
{
    internal static class AacHuffman
    {
        public static int ReadScaleFactor(ref AacBitReader bits) => ReadSymbol(ref bits, 0) - 60;

        public static int ReadSpectrum(ref AacBitReader bits, int codebook, scoped Span<int> values)
        {
            (int dimensions, int radix, int offset, bool signed) = codebook switch
            {
                1 or 2 => (4, 3, 1, true),
                3 or 4 => (4, 3, 0, false),
                5 or 6 => (2, 9, 4, true),
                7 or 8 => (2, 8, 0, false),
                9 or 10 => (2, 13, 0, false),
                11 => (2, 17, 0, false),
                _ => throw new ArgumentOutOfRangeException(nameof(codebook))
            };
            if (values.Length < dimensions)
            {
                throw new ArgumentException("The spectral tuple buffer is too small.", nameof(values));
            }
            int symbol = ReadSymbol(ref bits, codebook);
            for (int index = dimensions - 1; index >= 0; index--)
            {
                values[index] = symbol % radix - offset;
                symbol /= radix;
            }
            if (!signed)
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
            int node = 0;
            for (int length = 0; length < 19; length++)
            {
                node = tree[node * 2 + bits.Read(1)];
                if (node < 0)
                {
                    return -node - 1;
                }
            }
            throw new InvalidDataException("The AAC Huffman codeword is invalid.");
        }
    }
}
