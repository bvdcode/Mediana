// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.Tests
{
    public class PdfFontResolverTests
    {
        private static PdfReaderOptions Fonts => new() { FontResolver = _ => BundledFontPrograms.GetFont("Canada1500") };
        private static PdfRenderOptions Render => new() { Dpi = 72, Supersampling = 1 };

        [Fact]
        public async Task UnembeddedFontRequiresAnExplicitResolverAndProducesWarnings()
        {
            byte[] input = Simple("BT /F 20 Tf 10 40 Td (Hello) Tj ET");
            using PdfDocument strict = await PdfReader.ReadAsync(new MemoryStream(input));
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(strict, 1, Render));
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input), Fonts);
            using PdfPage page = document.GetPage(1);
            Assert.Empty(page.UnsupportedFeatures);
            Assert.Contains(page.Warnings, warning => warning.Contains("caller-supplied", StringComparison.Ordinal));
            using PixelBuffer image = PdfRenderer.Render(page, Render);
            Assert.Contains(image.Pixels.ToArray(), value => value == 0);
        }

        [Fact]
        public async Task DeclaredAdvancesRemainAuthoritativeWithAProvidedFont()
        {
            const string widths = "/FirstChar 65 /LastChar 65 /Widths [2000]";
            using PdfDocument actual = await PdfReader.ReadAsync(new MemoryStream(Simple("BT /F 10 Tf 10 40 Td (AA) Tj ET", widths)), Fonts);
            using PdfDocument expected = await PdfReader.ReadAsync(new MemoryStream(Simple("BT /F 10 Tf 10 40 Td (A) Tj 20 0 Td (A) Tj ET", widths)), Fonts);
            using PixelBuffer image = PdfRenderer.Render(actual, 1, Render);
            using PixelBuffer reference = PdfRenderer.Render(expected, 1, Render);
            Assert.Equal(reference.Pixels.ToArray(), image.Pixels.ToArray());
        }

        [Theory]
        [InlineData("<0001> <0002> <0410>", "<00010002>", false)]
        [InlineData("<0000> <FFFF> <0000>", "<04100411>", true)]
        public async Task CompositeFontsUseToUnicodeAndWarnOnRecoveredRanges(string range, string text, bool recovered)
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Composite(range, text)), Fonts);
            using PdfPage page = document.GetPage(1);
            Assert.Empty(page.UnsupportedFeatures);
            Assert.Equal(recovered, page.Warnings.Any(warning => warning.Contains("byte boundary", StringComparison.Ordinal)));
            using PixelBuffer image = PdfRenderer.Render(page, Render);
            Assert.Contains(image.Pixels.ToArray(), value => value == 0);
        }

        [Fact]
        public async Task MissingMappingsCannotSilentlySelectAGlyphWithTheSameNumericCode()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Composite("<0001> <0002> <0410>", "<0410>")), Fonts);
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(document, 1, Render));
        }

        [Fact]
        public async Task ResolvedFontProgramsAreOwnedByTheDetachedPage()
        {
            byte[] program = BundledFontPrograms.GetFont("Canada1500").ToArray();
            PdfReaderOptions options = new() { FontResolver = _ => program };
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Simple("BT /F 20 Tf 10 40 Td (A) Tj ET")), options);
            using PdfPage page = document.GetPage(1);
            Array.Clear(program);
            document.Dispose();
            using PixelBuffer image = PdfRenderer.Render(page, Render);
            Assert.Contains(image.Pixels.ToArray(), value => value == 0);
        }

        [Fact]
        public async Task LargeRecoveredMappingsRespectTheOperationLimit()
        {
            PdfReaderOptions options = Fonts with { MaximumOperations = 1000 };
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Composite("<0000> <FFFF> <0000>", "<0410>")), options);
            Assert.Throws<InvalidDataException>(() => PdfRenderer.Render(document, 1, Render));
        }

        [Fact]
        public Task PublicImageIntegrationReceivesWarningsThroughTheRequest() =>
            PdfNonPaintingAnnotationTestInput.VerifyFontIntegrationAsync();

        [Fact]
        public async Task ExpandedCidWidthsRespectThePageOperationLimit()
        {
            PdfReaderOptions options = Fonts with { MaximumOperations = 10000 };
            byte[] input = Composite("<0001> <0002> <0410>", "<0001>", "/W [0 65535 1000]", "<0001> <0002>");
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input), options);
            Assert.Throws<InvalidDataException>(() => PdfRenderer.Render(document, 1, Render));
        }

        [Fact]
        public async Task TransientFontProgramsDoNotAccumulateAcrossRepeatedPageInterpretation()
        {
            ReadOnlyMemory<byte> program = BundledFontPrograms.GetFont("Canada1500");
            PdfReaderOptions options = new() { FontResolver = _ => program, MaximumDecodedBytes = program.Length + 1024 };
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Simple("BT /F 10 Tf 10 40 Td (A) Tj ET")), options);
            using PixelBuffer first = PdfRenderer.Render(document, 1, Render);
            using PixelBuffer second = PdfRenderer.Render(document, 1, Render);
            Assert.Equal(first.Pixels.ToArray(), second.Pixels.ToArray());
        }

        private static byte[] Simple(string content, string entries = "") => PdfTestDocument.Create(content,
            resources: "/Font << /F 5 0 R >>", extraObjects: PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " + entries + " >>"));

        private static byte[] Composite(string range, string text, string widths = "", string space = "<0000> <FFFF>") => PdfTestDocument.Create("BT /F 20 Tf 10 40 Td " + text + " Tj ET",
            resources: "/Font << /F 5 0 R >>", extraObjects:
            [PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type0 /BaseFont /Arial /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>"),
            PdfTestDocument.Bytes("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Arial /DW 1000 " + widths + " >>"),
            PdfTestDocument.Stream(PdfTestDocument.Bytes("begincmap 1 begincodespacerange " + space + " endcodespacerange 1 beginbfrange "
                + range + " endbfrange endcmap"))]);
    }
}
