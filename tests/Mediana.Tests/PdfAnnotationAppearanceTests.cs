// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PdfAnnotationAppearanceTests
    {
        [Theory]
        [InlineData("", "6 0 R")]
        [InlineData("/Matrix [0 1 -1 0 20 0]", "6 0 R")]
        [InlineData("", "<< /On 6 0 R >>")]
        public async Task AppearanceFitsTheRectangleAndPaintsAfterPageContents(string matrix, string normal)
        {
            byte[] input = PdfTestDocument.Create("0 0 1 rg 0 0 200 100 re f 0 0 1 1 re W n 2 0 0 2 50 50 cm",
                pageEntries: "/Annots [5 0 R]", extraObjects:
                [PdfTestDocument.Bytes("<< /Subtype /Widget /Rect [10 20 50 40] /AS /On /AP << /N " + normal + " >> >>"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 0 0 20 10 re f"),
                    "/Subtype /Form /BBox [0 0 20 10] /Resources << >> " + matrix)]);
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
            using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.Equal(new Rgba32(255, 0, 0), image[20, 70]);
            Assert.Equal(new Rgba32(0, 0, 255), image[5, 70]);
            Assert.Equal(new Rgba32(0, 0, 255), image[55, 70]);
        }

        [Fact]
        public async Task MissingReferencedAppearanceIsInvalidInput()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfNonPaintingAnnotationTestInput.Create(
                "/Subtype /Link /Rect [0 0 10 10] /AP << /N 6 0 R >>")));
            Assert.Throws<InvalidDataException>(() => PdfRenderer.Render(document, 1));
        }

        [Fact]
        public async Task StateDictionaryRequiresAnExplicitAppearanceState()
        {
            byte[] input = PdfTestDocument.Create("", pageEntries: "/Annots [5 0 R]", extraObjects:
                [PdfTestDocument.Bytes("<< /Subtype /Widget /Rect [10 20 50 40] /AP << /N << /On 6 0 R >> >> >>"),
                PdfTestDocument.Stream([], "/Subtype /Form /BBox [0 0 20 10]")]);
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(document, 1));
        }
    }
}
