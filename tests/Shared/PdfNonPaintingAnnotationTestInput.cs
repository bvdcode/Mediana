// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Codecs;
using Mediana.Core.Video;
using Mediana.Drawing.Fonts;
using Mediana.Core.Memory;
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

        internal static byte[] SoftMask(bool luminosity = true, bool invert = false, string? page = null)
        {
            string subtype = luminosity ? "Luminosity" : "Alpha";
            string content = luminosity ? "/Fade sh" : "0 0 100 100 re f";
            return PdfTestDocument.Create(page ?? "/M gs 1 0 0 rg 0 0 200 100 re f",
                resources: "/ExtGState << /M << /SMask << /S /" + subtype + " /G 5 0 R "
                    + (invert ? "/TR << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>" : "")
                    + " >> >> /N << /SMask /None >> >>",
                extraObjects: PdfTestDocument.Stream(PdfTestDocument.Bytes(content),
                    "/Subtype /Form /BBox [0 0 200 100] /Group << /S /Transparency /I true /CS /DeviceGray >> "
                    + "/Resources << /Shading << /Fade << /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] "
                    + "/Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >> >> >>"));
        }

        internal static async Task VerifyGroupOpacityAsync()
        {
            byte[] input = PdfTestDocument.Create("/S gs /X Do",
                resources: "/ExtGState << /S << /ca 0.5 >> >> /XObject << /X 5 0 R >>",
                extraObjects: PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 10 10 40 40 re f 20 20 40 40 re f"),
                    "/Subtype /Form /BBox [0 0 200 100] /Group << /S /Transparency /I true >>"));
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
            using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            if (image[15, 85] != image[30, 70] || image[30, 70].G is < 127 or > 128)
            { throw new InvalidDataException("PDF group opacity was applied more than once."); }
        }

        internal static async Task VerifySoftMaskAsync(bool luminosity, bool invert)
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(SoftMask(luminosity, invert)));
            using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            byte left = image[50, 50].G, right = image[150, 50].G;
            if (luminosity)
            {
                int expectedLeft = invert ? 64 : 191, expectedRight = invert ? 192 : 63;
                if (Math.Abs(left - expectedLeft) > 2 || Math.Abs(right - expectedRight) > 2)
                { throw new InvalidDataException("PDF luminosity mask or transfer produced incorrect opacity."); }
            }
            else if (left != (invert ? 255 : 0) || right != (invert ? 0 : 255))
            { throw new InvalidDataException("PDF alpha mask or transfer produced incorrect opacity."); }
        }

        internal static async Task VerifyMaskBudgetAsync()
        {
            const string page = "/M gs 1 0 0 rg 0 0 200 50 re f 0 50 200 50 re f";
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(SoftMask(page: page)));
            MediaBufferAllocator allocator = new(240000);
            using (PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 }, allocator: allocator))
            {
                if (allocator.AllocatedBytes != 80000) { throw new InvalidDataException("PDF masks or layers retained temporary storage."); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("PDF output retained its memory budget."); }
            MediaBufferAllocator limited = new(160000);
            bool rejected = false;
            try
            {
                using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 }, allocator: limited);
            }
            catch (MediaMemoryLimitException)
            { rejected = true; }
            if (!rejected || limited.AllocatedBytes != 0) { throw new InvalidDataException("PDF temporary raster memory exceeded or retained its budget."); }
        }

        internal static async Task VerifyFontIntegrationAsync()
        {
            byte[] input = PdfTestDocument.Create("BT /F 20 Tf 10 40 Td (A) Tj ET",
                resources: "/Font << /F 5 0 R >>",
                extraObjects: PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
            PdfReaderOptions reader = new() { Text = new() { FontResolver = _ => BundledFontPrograms.GetFont("Canada1500") } };
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new PdfImageDecoder(reader, new() { Dpi = 72, Supersampling = 1 })).Build();
            MediaBufferAllocator allocator = new(1000000);
            List<string> warnings = [];
            using (VideoFrame frame = await engine.DecodeImageAsync(input, allocator, new ImageDecodeRequest { WarningHandler = warnings.Add }))
            {
                if (!warnings.Any(warning => warning.Contains("caller-supplied", StringComparison.Ordinal))
                    || warnings.Count != warnings.Distinct(StringComparer.Ordinal).Count() || frame.Format.Width != 200)
                { throw new InvalidDataException("PDF image integration did not deliver font diagnostics."); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("PDF image integration retained output buffers."); }
            bool rejected = false;
            try
            {
                using VideoFrame frame = await engine.DecodeImageAsync(input, allocator,
                    new ImageDecodeRequest { WarningHandler = _ => throw new InvalidOperationException("Warning delivery failed.") });
            }
            catch (InvalidOperationException error) when (error.Message == "Warning delivery failed.")
            { rejected = true; }
            if (!rejected || allocator.AllocatedBytes != 0)
            { throw new InvalidDataException("PDF diagnostic callback failure retained output buffers."); }
        }
    }
}
