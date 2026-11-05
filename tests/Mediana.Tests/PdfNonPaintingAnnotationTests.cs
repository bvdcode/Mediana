// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    public class PdfNonPaintingAnnotationTests
    {
        [Fact]
        public Task HiddenEmptyAndBorderlessLinkAnnotationsDoNotRequireAppearances() => PdfNonPaintingAnnotationTestInput.VerifyAsync();

        [Theory]
        [InlineData("/Subtype /Link /Rect [0 0 10 10]")]
        [InlineData("/Subtype /Link /Rect [0 0 10 10] /Border [0 0 0] /BS << /W 1 >>")]
        [InlineData("/Subtype /Square /Rect [0 0 10 10] /F 1")]
        public async Task VisibleAnnotationSemanticsRemainExplicit(string entries)
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfNonPaintingAnnotationTestInput.Create(entries)));
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(document, 1));
        }

        [Fact]
        public Task NonIsolatedFormGroupWithNormalCompositionKeepsTheBackdrop() => PdfNonPaintingAnnotationTestInput.VerifyFormAsync();

        [Theory]
        [InlineData("/Group << /S /Transparency /I true >>", "")]
        [InlineData("/Group << /S /Transparency /K true >>", "")]
        [InlineData("/Group << /S /Transparency >>", "/S gs")]
        public async Task FormGroupsRenderThroughSeparateComposition(string group, string state)
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfNonPaintingAnnotationTestInput.Form(group, state)));
            using Mediana.Drawing.PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.Equal(255, image[20, 80].R);
            Assert.InRange(image[20, 80].G, state.Length == 0 ? (byte)0 : (byte)127, state.Length == 0 ? (byte)0 : (byte)128);
        }
    }
}
