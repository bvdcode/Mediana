// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PdfInvisibleTextTestInput
    {
        internal static byte[] Create(int mode = 3) => PdfTestDocument.Create(
            $"1 0 0 rg BT /F 20 Tf 1 0 0 1 10 20 Tm {mode} Tr (A) Tj /T 20 Tf 0 Tr (A) Tj ET",
            resources: "/Font << /F 5 0 R /T 6 0 R >>", extraObjects:
            [
                PdfTestDocument.Bytes("<< /Type /Font /Subtype /TrueType /BaseFont /Preview /FirstChar 65 /LastChar 65 /Widths [500] /FontDescriptor << /MissingWidth 0 >> >>"),
                PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type3 /FontBBox [0 0 500 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /A 7 0 R >> /Encoding << /Differences [65 /A] >> /FirstChar 65 /LastChar 65 /Widths [500] >>"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes("500 0 d0 0 0 500 1000 re f"))
            ]);

        internal static async Task VerifyAsync()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Create()));
            using PdfPage page = document.GetPage(1);
            using PixelBuffer image = PdfRenderer.Render(page, new() { Dpi = 72, Supersampling = 1 });
            if (page.UnsupportedFeatures.Count != 0 || image[15, 70] != new Rgba32(255, 255, 255)
                || image[25, 70] != new Rgba32(255, 0, 0) || image[35, 70] != new Rgba32(255, 255, 255))
            {
                throw new InvalidDataException("Invisible PDF text must preserve advance widths without requiring glyph outlines.");
            }
        }
    }
}
