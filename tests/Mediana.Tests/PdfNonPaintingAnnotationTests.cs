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
        public async Task FormGroupsRequiringSeparateCompositionRemainExplicit(string group, string state)
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfNonPaintingAnnotationTestInput.Form(group, state)));
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(document, 1));
        }
    }
}
