// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PdfInvisibleTextTests
    {
        [Fact]
        public Task InvisibleUnembeddedTextAdvancesWithoutDrawing() => PdfInvisibleTextTestInput.VerifyAsync();

        [Fact]
        public async Task VisibleUnembeddedTextStillRequiresOutlines()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfInvisibleTextTestInput.Create(0)));
            using PdfPage page = document.GetPage(1);
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(page));
        }
    }
}
