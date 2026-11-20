// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Tests;
using Xunit;

namespace Mediana.Documents.Tests
{
    public class PdfUnicodeRangeTests
    {
        [Fact]
        public void CompactRangesResolveOnlyUsedGlyphsAndPreservePixels() => DocumentReadersTestInput.VerifyCompactUnicode();

        [Theory]
        [InlineData("<00FE> <0102> <00FE>", "00FE00FF010001010102", "þÿĀāĂ")]
        [InlineData("<0001> <0003> <D83DDE00>", "000100020003", "😀😁😂")]
        [InlineData("<0001> <0003> <00660069>", "000100020003", "fifjfk")]
        [InlineData("<0001> <0003> [<0041> <00660069> <D83DDE00>]", "000100020003", "Afi😀")]
        public void SequentialAndExplicitRangesPreserveUnicodeStrings(string mapping, string codes, string expected)
        {
            using PdfDocument document = PdfReader.Open(PdfTextTestData.Composite("begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange " + mapping + " endbfrange endcmap", codes));
            Assert.Equal(expected, document.ReadText().Single().Text);
        }

        [Theory]
        [InlineData("1 begincodespacerange <0000> <FFFF> endcodespacerange 2 beginbfrange <0000> <0100> <0000> <0100> <0200> <0100> endbfrange")]
        [InlineData("2 begincodespacerange <0000> <0001> <0003> <0004> endcodespacerange 1 beginbfrange <0000> <0004> <0041> endbfrange")]
        [InlineData("1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange <0001> <0002> <FFFF> endbfrange")]
        [InlineData("1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange <0001> <0002> [<0041>] endbfrange")]
        [InlineData("1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange <0002> <0001> <0041> endbfrange")]
        public void InvalidRangesFailBeforeExtractingTheirFirstCharacter(string cmap)
        {
            using PdfDocument document = PdfReader.Open(PdfTextTestData.Composite("begincmap " + cmap + " endcmap", "0001"));
            Assert.Throws<InvalidDataException>(() => document.ReadText().First());
        }

        [Fact]
        public void AdjacentAndOverlappingCodeSpacesAreValidatedAsAUnion()
        {
            using PdfDocument document = PdfReader.Open(PdfTextTestData.Composite("begincmap 3 begincodespacerange <0000> <0001> <0002> <0003> <0001> <0002> endcodespacerange 1 beginbfrange <0000> <0003> <0041> endbfrange endcmap", "0000000100020003"));
            Assert.Equal("ABCD", document.ReadText().Single().Text);
        }

        [Fact]
        public void LargeRangesRetainOperationLimitsWithoutAllocatingPerCode()
        {
            byte[] encoded = PdfTextTestData.Composite("begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange <0000> <FFFF> <0000> endbfrange endcmap");
            using PdfDocument limited = PdfReader.Open(encoded, new() { MaximumOperations = 100 });
            Assert.Throws<InvalidDataException>(() => limited.ReadText().First());
            using PdfDocument document = PdfReader.Open(encoded);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal("AB‣", document.ReadText().Single().Text);
            Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 1, 256 * 1024);
        }
    }
}
