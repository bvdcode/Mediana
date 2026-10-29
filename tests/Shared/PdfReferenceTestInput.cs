// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;
namespace Mediana.Tests
{
    internal static class PdfReferenceTestInput
    {
        internal static async Task VerifyJp2Async(string root)
        {
            byte[] jp2 = await File.ReadAllBytesAsync(Path.Combine(root, "RGB-129x101.jp2"));
            byte[] image = PdfTestDocument.Stream(jp2, "/Type /XObject /Subtype /Image /Width 129 /Height 101 /Filter /JPXDecode");
            byte[] pdf = PdfTestDocument.Create("q 100 0 0 50 0 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>", extraObjects: image);
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(pdf));
            PdfImage pixels = document.GetPage(1).Images.Single();
            byte[] rgb = await File.ReadAllBytesAsync(Path.Combine(root, "RGB-129x101.raw"));
            if (pixels.Width != 129 || pixels.Height != 101) { throw new InvalidDataException("PDF JP2 dimensions differ."); }
            for (int i = 0; i < rgb.Length / 3; i++)
            {
                if (rgb[i * 3] != pixels.Pixels.Span[i * 4] || rgb[i * 3 + 1] != pixels.Pixels.Span[i * 4 + 1] || rgb[i * 3 + 2] != pixels.Pixels.Span[i * 4 + 2] || pixels.Pixels.Span[i * 4 + 3] != 255)
                { throw new InvalidDataException("PDF JP2 pixels differ from the independent RGB source."); }
            }
        }
        internal static async Task VerifyAsync(string root)
        {
            await PdfStructureTestInput.VerifyAsync();
            await VerifyJp2Async(Path.GetFullPath(Path.Combine(root, "..", "Jpeg2000")));
            using PdfDocument report = await PdfReader.ReadFileAsync(Path.Combine(root, "report.pdf"));
            PdfPage reportPage = report.GetPage(1);
            if (reportPage.Images.Count != 1) { throw new InvalidDataException("PDF report image extraction differs."); }
            await PdfPixelReference.VerifyAsync(root, "report", PdfRenderer.Render(reportPage));
            using PdfDocument scan = await PdfReader.ReadFileAsync(Path.Combine(root, "scan-globals.pdf"));
            PdfPage scanPage = scan.GetPage(1);
            PdfImage scanImage = scanPage.Images.Single();
            PixelBuffer scanPixels = new(scanImage.Width, scanImage.Height);
            scanImage.Pixels.CopyTo(scanPixels.Pixels);
            await PdfPixelReference.VerifyAsync(root, "scan", scanPixels, exact: true);
            using PdfDocument document = await PdfReader.ReadFileAsync(Path.Combine(root, "graphics.pdf"));
            if (document.PageCount != 2) { throw new InvalidDataException("PDF page count differs."); }
            PdfPage page = document.GetPage(1);
            PixelBuffer image = PdfRenderer.Render(page, new() { Dpi = 72, Supersampling = 1 });
            if (image[20, 180] != new Rgba32(255, 0, 0) || image[50, 150] != new Rgba32(0, 0, 255) || image[120, 170] != new Rgba32(0, 255, 0) || image[105, 170] != new Rgba32(255, 255, 255))
            { throw new InvalidDataException("PDF vector painting or clipping differs."); }
            await PdfPixelReference.VerifyAsync(root, "graphics", PdfRenderer.Render(page));
            PdfPage second = document.GetPage(2);
            document.Dispose();
            PixelBuffer other = PdfRenderer.Render(second, new() { Dpi = 72, Supersampling = 1 });
            if (Math.Abs(other[30, 30].R - 128) > 1 || Math.Abs(other[30, 30].B - 128) > 1 || other[170, 30] != new Rgba32(0, 0, 255))
            { throw new InvalidDataException("PDF transparency or detached page rendering differs."); }
        }
    }
}
