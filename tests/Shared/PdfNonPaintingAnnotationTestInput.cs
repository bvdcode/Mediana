// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PdfNonPaintingAnnotationTestInput
    {
        internal static byte[] Create(string entries) => PdfTestDocument.Create("1 0 0 rg 10 10 20 20 re f",
            pageEntries: "/Annots [5 0 R]", extraObjects: PdfTestDocument.Bytes("<< /Type /Annot " + entries + " >>"));

        internal static async Task VerifyAsync()
        {
            foreach (string annotation in new[]
            {
                "/Subtype /Link /Rect [0 0 10 10] /Border [0 0 0]",
                "/Subtype /Link /Rect [0 0 10 10] /BS << /W 0 >>",
                "/Subtype /Square /Rect [0 0 10 10] /F 2",
                "/Subtype /Square /Rect [0 0 10 10] /F 32",
                "/Subtype /Square /Rect [0 0 0 10]"
            })
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Create(annotation)));
                using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
                if (image[20, 80] != new Rgba32(255, 0, 0)) { throw new InvalidDataException("Non-painting annotations changed page graphics."); }
            }
        }

        internal static byte[] Form(string group = "/Group << /S /Transparency >>", string state = "") =>
            PdfTestDocument.Create(state + " /X Do", resources: "/XObject << /X 5 0 R >> /ExtGState << /S << /ca 0.5 >> >>",
                extraObjects: PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 10 10 20 20 re f"),
                    "/Type /XObject /Subtype /Form /BBox [0 0 200 100] /Resources << >> " + group));

        internal static async Task VerifyFormAsync()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Form()));
            using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            if (image[20, 80] != new Rgba32(255, 0, 0)) { throw new InvalidDataException("Non-isolated PDF form group changed page graphics."); }
        }
    }
}
