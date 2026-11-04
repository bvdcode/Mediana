// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    public class PdfPageGroupTests
    {
        [Fact]
        public Task PageGroupIsIsolatedAndCompositedOverTheOutputMedium() => PdfPageGroupTestInput.VerifyAsync();

        [Theory]
        [InlineData("/Group << /S /Transparency /K true >>")]
        [InlineData("/Group << /S /Transparency /CS /DeviceCMYK >>")]
        public async Task UnsupportedGroupSemanticsRemainExplicit(string group)
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfPageGroupTestInput.Create(group)));
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(document, 1));
        }
    }
}
