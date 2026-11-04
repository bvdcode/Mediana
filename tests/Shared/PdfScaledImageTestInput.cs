// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PdfScaledImageTestInput
    {
        private static readonly PdfRenderOptions Preview = new() { Dpi = 72, Supersampling = 1, MaximumWidth = 20 };

        internal static async Task VerifyBudgetsAsync(string root)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(root, "l-s0-35x19.jpg"));
            byte[] pdf = Create(jpeg, 35, 19, "q 100 0 0 50 0 0 cm /I Do Q");
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(pdf), new() { MaximumImageBytes = 960 });
            Reject<InvalidDataException>(() => document.GetPage(1));
            MediaBufferAllocator allocator = new(800);
            using (PixelBuffer image = PdfRenderer.Render(document, 1, Preview, allocator: allocator))
            {
                Require(image.Width == 20 && image.Height == 10 && image[15, 2] == new Rgba32(255, 255, 255));
            }
            Require(allocator.AllocatedBytes == 0);
            using (VideoFrame frame = await new PdfImageDecoder(new() { MaximumImageBytes = 960 }, Preview)
                .DecodeAsync(pdf, new ImageDecodeRequest(), allocator))
            {
                Require(frame.Format.Width == 20 && frame.Format.Height == 10);
            }
            Require(allocator.AllocatedBytes == 0);
            using PdfDocument shortBudget = await PdfReader.ReadAsync(new MemoryStream(pdf), new() { MaximumImageBytes = 959 });
            Reject<MediaMemoryLimitException>(() => PdfRenderer.Render(shortBudget, 1, Preview));
            using PdfDocument sourceLimit = await PdfReader.ReadAsync(new MemoryStream(pdf), new() { MaximumImagePixels = 664 });
            Reject<InvalidDataException>(() => PdfRenderer.Render(sourceLimit, 1, Preview));
        }

        internal static async Task VerifyMultiplePlacementsAsync(string root)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(root, "l-s0-35x19.jpg"));
            byte[] pdf = Create(jpeg, 35, 19, "q 20 0 0 10 0 0 cm /I Do Q q 100 0 0 50 50 0 cm /I Do Q");
            using PdfDocument sufficient = await PdfReader.ReadAsync(new MemoryStream(pdf), new() { MaximumImageBytes = 1020 });
            using PixelBuffer image = PdfRenderer.Render(sufficient, 1, Preview);
            using PdfDocument insufficient = await PdfReader.ReadAsync(new MemoryStream(pdf), new() { MaximumImageBytes = 800 });
            Reject<MediaMemoryLimitException>(() => PdfRenderer.Render(insufficient, 1, Preview));
        }

        internal static async Task VerifyOwnershipAsync()
        {
            byte[] pdf = Create(JpegTestData.Image(), 1, 1, "/I Do");
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(pdf));
            PdfPage page = document.GetPage(1);
            PdfImage image = page.Images.Single();
            MediaBufferAllocator allocator = new(4);
            using (VideoFrame frame = image.ToVideoFrame(allocator))
            {
                page.Dispose();
                page.Dispose();
                Reject<ObjectDisposedException>(() => { _ = image.Pixels; });
                Reject<ObjectDisposedException>(() => PdfRenderer.Render(page, Preview));
                Require(frame.Data.Span.SequenceEqual(new byte[] { 128, 128, 128, 255 }));
            }
            Require(allocator.AllocatedBytes == 0);
        }

        internal static async Task VerifyLargeScanAsync()
        {
            const int width = 5100, height = 7014;
            byte[] frame = [8, 0, 0, 0, 0, 1, 1, 17, 0];
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), height);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), width);
            byte[] quantization = new byte[65];
            quantization.AsSpan(1).Fill(1);
            byte[] dc = new byte[18]; dc[1] = 1;
            byte[] ac = dc.ToArray(); ac[0] = 16;
            int bitCount = ((width + 7) / 8) * ((height + 7) / 8) * 2;
            byte[] entropy = new byte[(bitCount + 7) / 8];
            int remaining = entropy.Length * 8 - bitCount;
            entropy[^1] |= (byte)((1 << remaining) - 1);
            byte[] jpeg = [255, 216, .. JpegTestData.Segment(219, quantization), .. JpegTestData.Segment(192, frame),
                .. JpegTestData.Segment(196, [.. dc, .. ac]), .. JpegTestData.Segment(218, [1, 1, 0, 0, 63, 0]), .. entropy, 255, 217];
            byte[] pdf = Create(jpeg, width, height, "q 200 0 0 100 0 0 cm /I Do Q");
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(pdf), new() { MaximumImageBytes = 3 * 1024 * 1024 });
            using PixelBuffer image = PdfRenderer.Render(document, 1, Preview);
            Require(image.Width == 20 && image.Height == 10 && image[10, 5] == new Rgba32(128, 128, 128));
        }

        private static byte[] Create(byte[] jpeg, int width, int height, string content) => PdfTestDocument.Create(content,
            resources: "/XObject << /I 5 0 R >>", extraObjects: PdfTestDocument.Stream(jpeg,
                $"/Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent 8 /ColorSpace /DeviceGray /Filter /DCTDecode"));

        private static void Reject<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidDataException("PDF rendering accepted an invalid image budget or lifetime.");
        }
        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("PDF preview dimensions, sample reduction or ownership differ."); }
        }
    }
}
