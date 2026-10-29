// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacEntropyTests
    {
        [Fact]
        public void BitsCrossBytesWithoutLosingTheFollowingField()
        {
            AacBitReader bits = new([0xab, 0xcd, 0xef, 0x12]);
            Assert.Equal(10, bits.Read(4));
            Assert.Equal(0xbcdef, bits.Read(20));
            Assert.Equal(0x12, bits.Read(8));
            Assert.Equal(0, bits.Remaining);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("100", -1)]
        [InlineData("1010", 1)]
        [InlineData("111111111111101000", -60)]
        [InlineData("1111111111111110011", 60)]
        public void ScaleFactorCodewordsIncludeBothExtremes(string codeword, int expected)
        {
            AacBitReader bits = new(Bytes(codeword + "10101"));
            Assert.Equal(expected, AacHuffman.ReadScaleFactor(ref bits));
            Assert.Equal(21, bits.Read(5));
        }

        [Theory]
        [InlineData(1, "11111111000", new[] { -1, -1, -1, -1 })]
        [InlineData(1, "0", new[] { 0, 0, 0, 0 })]
        [InlineData(1, "11111110100", new[] { 1, 1, 1, 1 })]
        [InlineData(3, "1111111111110101010", new[] { -2, 2, -2, 2 })]
        [InlineData(5, "1111111111111", new[] { -4, -4 })]
        [InlineData(5, "1111111111110", new[] { 4, 4 })]
        [InlineData(7, "11111111111110", new[] { -7, 7 })]
        [InlineData(9, "11111111111111101", new[] { 12, -12 })]
        [InlineData(11, "0010010100000100000", new[] { -33, 16 })]
        public void SpectralTuplesReadSignsBeforeEscapeMagnitudes(int codebook, string codeword, int[] expected)
        {
            AacBitReader bits = new(Bytes(codeword + "10101"));
            Span<int> tuple = stackalloc int[4];
            int count = AacHuffman.ReadSpectrum(ref bits, codebook, tuple);
            Assert.Equal(expected, tuple[..count].ToArray());
            Assert.Equal(21, bits.Read(5));
        }

        [Fact]
        public void AllNormativeCodebooksAreCompleteAndPrefixFree()
        {
            int[] counts = [121, 81, 81, 81, 81, 81, 81, 64, 64, 169, 169, 289];
            for (int codebook = 0; codebook < counts.Length; codebook++)
            {
                HashSet<int> leaves = [];
                Walk(AacHuffmanTables.Get(codebook), 0, 0, leaves);
                Assert.Equal(Enumerable.Range(0, counts[codebook]), leaves.Order());
            }
        }

        [Fact]
        public void EveryCodewordDecodesAtEveryBitAlignmentAndAtEndOfInput()
        {
            for (int codebook = 0; codebook < 12; codebook++)
            {
                VerifyCodewords(AacHuffmanTables.Get(codebook), codebook, 0, string.Empty);
            }
        }

        private static void VerifyCodewords(ReadOnlySpan<short> tree, int codebook, int node, string prefix)
        {
            for (int branch = 0; branch < 2; branch++)
            {
                int child = tree[node * 2 + branch];
                string codeword = prefix + (branch == 0 ? "0" : "1");
                if (child >= 0)
                {
                    VerifyCodewords(tree, codebook, child, codeword);
                    continue;
                }
                for (int alignment = 0; alignment < 8; alignment++)
                {
                    AacBitReader bits = new(Bytes(new string('1', alignment) + codeword + "10101"));
                    bits.Skip(alignment);
                    Assert.Equal(-child - 1, AacHuffman.ReadSymbol(ref bits, codebook));
                    Assert.Equal(alignment + codeword.Length, bits.Position);
                    Assert.Equal(21, bits.Read(5));
                }
                int padding = (8 - codeword.Length % 8) % 8;
                AacBitReader tail = new(Bytes(new string('1', padding) + codeword));
                tail.Skip(padding);
                Assert.Equal(-child - 1, AacHuffman.ReadSymbol(ref tail, codebook));
                Assert.Equal(0, tail.Remaining);
                if (codebook != 0)
                {
                    VerifySpectralTuple(codebook, -child - 1, codeword);
                }
            }
        }

        private static void VerifySpectralTuple(int codebook, int symbol, string codeword)
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
            int[] expected = new int[dimensions];
            for (int index = dimensions - 1; index >= 0; index--)
            {
                expected[index] = symbol % radix - offset;
                symbol /= radix;
            }
            string suffix = string.Empty;
            if (!signed)
            {
                for (int index = 0; index < dimensions; index++)
                {
                    if (expected[index] != 0)
                    {
                        bool negative = index % 2 == 0;
                        suffix += negative ? "1" : "0";
                        if (negative)
                        {
                            expected[index] = -expected[index];
                        }
                    }
                }
            }
            if (codebook == 11)
            {
                foreach (int value in expected)
                {
                    if (Math.Abs(value) == 16)
                    {
                        suffix += "00000";
                    }
                }
            }
            Span<int> tuple = stackalloc int[4];
            for (int alignment = 0; alignment < 8; alignment++)
            {
                AacBitReader bits = new(Bytes(new string('1', alignment) + codeword + suffix + "10101"));
                bits.Skip(alignment);
                int count = AacHuffman.ReadSpectrum(ref bits, codebook, tuple);
                Assert.Equal(expected, tuple[..count].ToArray());
                Assert.Equal(21, bits.Read(5));
            }
        }

        [Fact]
        public void TruncatedCodewordsAndOverlongEscapesAreRejected()
        {
            Assert.Throws<InvalidDataException>(ReadTruncatedScaleFactor);
            Assert.Throws<InvalidDataException>(() => ReadEscape("0010000111111111"));
            Assert.Throws<InvalidDataException>(() => ReadEscape("001000010"));
        }

        private static void Walk(ReadOnlySpan<short> tree, int node, int depth, HashSet<int> leaves)
        {
            Assert.InRange(depth, 0, 18);
            for (int branch = 0; branch < 2; branch++)
            {
                int child = tree[node * 2 + branch];
                if (child < 0)
                {
                    Assert.True(leaves.Add(-child - 1));
                }
                else
                {
                    Assert.InRange(child, 1, tree.Length / 2 - 1);
                    Walk(tree, child, depth + 1, leaves);
                }
            }
        }

        private static void ReadTruncatedScaleFactor()
        {
            AacBitReader bits = new([0xff]);
            AacHuffman.ReadScaleFactor(ref bits);
        }

        private static void ReadEscape(string codeword)
        {
            AacBitReader bits = new(Bytes(codeword));
            AacHuffman.ReadSpectrum(ref bits, 11, stackalloc int[4]);
        }

        private static byte[] Bytes(string codeword)
        {
            byte[] data = new byte[(codeword.Length + 7) / 8];
            for (int index = 0; index < codeword.Length; index++)
            {
                if (codeword[index] == '1')
                {
                    data[index / 8] |= (byte)(1 << (7 - index % 8));
                }
            }
            return data;
        }
    }
}
