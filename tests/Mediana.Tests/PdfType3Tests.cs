// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    public class PdfType3Tests
    {
        [Fact]
        public async Task GlyphStreamsPreserveMetricsColorsAndState() => await PdfType3TestInput.VerifyPaintingAsync();

        [Theory]
        [InlineData("0 0 500 1000 re f")]
        [InlineData("500 1 d0")]
        [InlineData("500 0 d0 500 0 d0")]
        [InlineData("500 0 d0 q")]
        public async Task InvalidGlyphProgramsAreRejected(string glyph)
        {
            using MemoryStream input = new(PdfType3TestInput.Create(glyph));
            using PdfDocument document = await PdfReader.ReadAsync(input);
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Fact]
        public async Task GlyphOperationsRemainBounded()
        {
            using MemoryStream input = new(PdfType3TestInput.Create("500 0 d0 0 0 500 1000 re f"));
            using PdfDocument document = await PdfReader.ReadAsync(input, new() { MaximumOperations = 8 });
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }
    }
}
