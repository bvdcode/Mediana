// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PdfType3TestInput
    {
        internal static byte[] Create(string glyph, string matrix = "0.001 0 0 0.001 0 0", string width = "500")
        {
            byte[] font = PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [" + matrix
                + "] /CharProcs << /A 6 0 R >> /Encoding << /Differences [65 /A] >> /FirstChar 65 /LastChar 65 /Widths [" + width + "] >>");
            return PdfTestDocument.Create("/DeviceRGB cs 1 0 0 scn BT /F 20 Tf 1 0 0 1 10 20 Tm (AA) Tj ET 0 1 0 scn 40 20 10 20 re f",
                pageEntries: "/Annots []", resources: "/Font << /F 5 0 R >> /ColorSpace << /Ink /DeviceRGB >>",
                extraObjects: [font, PdfTestDocument.Stream(PdfTestDocument.Bytes(glyph))]);
        }

        internal static async Task VerifyPaintingAsync()
        {
            await VerifyAsync("500 0 0 0 500 1000 d1 0 0 1 rg 0 0 500 1000 re f", new(255, 0, 0));
            await VerifyAsync("500 0 0 0 500 1000 d1 /DeviceCMYK cs 1 0 0 0 scn 0 0 500 1000 re f", new(255, 0, 0));
            await VerifyAsync("500 0 d0 /Ink cs 0 0 1 scn 0 0 500 1000 re f", new(0, 0, 255));
            await VerifyAsync("0.5 0 d0 0 0 0.5 1 re f", new(255, 0, 0), matrix: "1 0 0 1 0 0", width: "0.5");
            await VerifyAsync("500 0 0 0 500 1000 d1", new(255, 255, 255));
            await VerifyAsync("500 0 15 265 150 77 d1", new(255, 255, 255));
        }

        private static async Task VerifyAsync(string glyph, Rgba32 ink, string matrix = "0.001 0 0 0.001 0 0", string width = "500")
        {
            using MemoryStream input = new(Create(glyph, matrix, width));
            using PdfDocument document = await PdfReader.ReadAsync(input);
            PdfPage page = document.GetPage(1);
            using PixelBuffer image = PdfRenderer.Render(page, new() { Dpi = 72, Supersampling = 1 });
            if (page.UnsupportedFeatures.Count != 0 || image[15, 70] != ink || image[25, 70] != ink
                || image[35, 70] != new Rgba32(255, 255, 255) || image[45, 70] != new Rgba32(0, 255, 0))
            {
                throw new InvalidDataException("Type 3 glyph streams must preserve font-space widths, inherited color and isolated graphics state.");
            }
        }
    }
}
