// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PdfRestartTestInput
    {
        internal static byte[] Create() => PdfTestDocument.Create("q 72 0 0 8 10 10 cm /I Do /I Do Q",
            resources: "/XObject << /I 5 0 R >>", extraObjects: PdfTestDocument.Stream(JpegRestartTestInput.Create(),
                "/Type /XObject /Subtype /Image /Width 72 /Height 8 /BitsPerComponent 8 /ColorSpace /DeviceGray /Filter /DCTDecode"));

        internal static async Task VerifyAsync()
        {
            byte[] encoded = Create();
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(encoded));
            using PdfPage page = document.GetPage(1);
            Require(page.Images.Count == 1 && page.Warnings.Count == 1 && page.Warnings[0] == JpegRestartTestInput.Warning);
            Require(document.Warnings.Count == 0 && page.UnsupportedFeatures.Count == 0);
            List<string> warnings = [];
            PdfRenderOptions options = new() { Dpi = 72, Supersampling = 1, WarningHandler = warnings.Add };
            using (PixelBuffer rendered = PdfRenderer.Render(document, 1, options))
            {
                Require(rendered[20, 85] == new Rgba32(128, 128, 128) && warnings.Count == 1 && warnings[0] == JpegRestartTestInput.Warning);
            }
            warnings.Clear();
            List<string> requested = [];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame frame = await new PdfImageDecoder(renderOptions: options).DecodeAsync(encoded, new() { WarningHandler = requested.Add }, allocator))
            {
                encoded.AsSpan().Clear();
                Require(warnings.Count == 1 && requested.Count == 1 && requested[0] == JpegRestartTestInput.Warning);
                Require(frame.GetRow(0, 85).Slice(20 * 4, 4).SequenceEqual(new byte[] { 128, 128, 128, 255 }));
            }
            Require(allocator.AllocatedBytes == 0);
            document.Dispose();
            warnings.Clear();
            using PixelBuffer detached = PdfRenderer.Render(page, options);
            Require(warnings.Count == 1 && detached[20, 85] == new Rgba32(128, 128, 128));
        }

        internal static async Task VerifyCallbackFailureAsync()
        {
            byte[] encoded = Create();
            MediaBufferAllocator allocator = new(1024 * 1024);
            bool stopped = false;
            try
            {
                using VideoFrame frame = await new PdfImageDecoder().DecodeAsync(encoded,
                    new() { WarningHandler = _ => throw new InvalidOperationException("Diagnostic consumer stopped.") }, allocator);
            }
            catch (InvalidOperationException error) when (error.Message == "Diagnostic consumer stopped.") { stopped = true; }
            Require(stopped && allocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("PDF JPEG recovery lost its warning, pixels or resource ownership."); }
        }
    }
}
