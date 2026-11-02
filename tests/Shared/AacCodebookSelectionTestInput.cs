// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    internal static class AacCodebookSelectionTestInput
    {
        internal static int VerifyTuples()
        {
            byte[] encoded = new byte[8192];
            int[] decoded = new int[1024];
            Span<int> tuple = stackalloc int[4];
            Span<int> values = stackalloc int[4];
            int cases = 0;
            int tiedCases = 0;
            for (int codebook = 1; codebook <= 11; codebook++)
            {
                AacSpectralCodebook book = AacSpectralCodebook.Get(codebook);
                int symbols = 0;
                foreach (short node in AacHuffmanTables.Get(codebook)) { symbols = Math.Max(symbols, -node); }
                int signs = book.Signed ? 1 : 1 << book.Dimensions;
                for (int symbol = 0; symbol < symbols; symbol++)
                {
                    book.Read(symbol, tuple);
                    for (int mask = 0; mask < signs; mask++)
                    {
                        for (int index = 0; index < values.Length; index++)
                        {
                            int component = index % book.Dimensions;
                            values[index] = (mask & (1 << component)) == 0 ? tuple[component] : -tuple[component];
                        }
                        if (Verify(values, encoded, decoded)) { tiedCases++; }
                        cases++;
                    }
                }
            }
            Require(tiedCases > 0);
            return cases;
        }

        internal static int VerifyBands()
        {
            byte[] encoded = new byte[8192];
            int[] decoded = new int[1024];
            int[] values = new int[1024];
            int cases = 0;
            Random random = new(160403);
            foreach (int length in new[] { 4, 8, 12, 16, 32, 64, 128, 256, 1024 })
            {
                foreach (int maximum in new[] { 0, 1, 2, 3, 4, 5, 7, 8, 12, 13, 15, 16, 17, 31, 32, 63, 64, 127, 128, 255, 256, 8191 })
                {
                    Span<int> band = values.AsSpan(0, length);
                    for (int variant = 0; variant < 6; variant++)
                    {
                        for (int index = 0; index < length; index++)
                        {
                            band[index] = variant switch
                            {
                                0 => index == length - 1 ? maximum : 0,
                                1 => index == 0 ? -maximum : 0,
                                2 => index % 2 == 0 ? maximum : -maximum,
                                3 => index % 4 == 0 ? -maximum : 0,
                                4 => random.Next(-maximum, maximum + 1),
                                5 => random.Next(0, maximum + 1),
                                _ => throw new ArgumentOutOfRangeException(nameof(variant))
                            };
                        }
                        _ = Verify(band, encoded, decoded);
                        cases++;
                    }
                }
            }
            return cases;
        }

        private static bool Verify(ReadOnlySpan<int> values, Span<byte> encoded, Span<int> decoded)
        {
            (byte book, int bits) = AacHuffmanEncoder.SelectCodebook(values);
            if (values.IndexOfAnyExcept(0) < 0)
            {
                Require(book == 0 && bits == 0);
                return false;
            }
            int expected = int.MaxValue;
            int expectedBook = 0;
            int matchingBooks = 0;
            for (int candidate = 1; candidate <= 11; candidate++)
            {
                int count = AacHuffmanEncoder.Count(candidate, values);
                if (count < expected) { expected = count; expectedBook = candidate; matchingBooks = 1; }
                else if (count == expected) { matchingBooks++; }
            }
            Require(book == expectedBook && bits == expected);
            AacBitWriter writer = new(encoded);
            AacHuffmanEncoder.Write(ref writer, book, values);
            Require(writer.Position == bits);
            AacBitReader reader = new(encoded);
            int dimensions = AacSpectralCodebook.Get(book).Dimensions;
            for (int start = 0; start < values.Length; start += dimensions)
            {
                Require(AacHuffman.ReadSpectrum(ref reader, book, decoded.Slice(start, dimensions)) == dimensions);
            }
            Require(reader.Position == bits && values.SequenceEqual(decoded[..values.Length]));
            return matchingBooks > 1;
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("AAC codebook selection changed exact costs, tie ordering, sign/escape bits or decoded values."); }
        }
    }
}
