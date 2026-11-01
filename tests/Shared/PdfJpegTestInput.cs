// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PdfJpegTestInput
    {
        internal static async Task VerifyCorpusAsync(string root)
        {
            using JsonDocument corpus = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "corpus.json")));
            foreach (JsonElement item in corpus.RootElement.EnumerateArray())
            {
                string name = item.GetProperty("name").GetString()!;
                using PdfDocument document = await PdfReader.ReadFileAsync(Path.Combine(root, name + ".pdf"));
                PdfPage page = document.GetPage(1);
                PdfImage image = page.Images.Single(value => value.Name == "Im0");
                Require(image.Width == item.GetProperty("width").GetInt32() && image.Height == item.GetProperty("height").GetInt32());
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".rgba"));
                VerifyPixels(image.Pixels.Span, expected, 4, name);
                document.Dispose();
                using PixelBuffer rendered = PdfRenderer.Render(page, new() { Dpi = 72, Supersampling = 1 });
                byte[] expectedPage = await File.ReadAllBytesAsync(Path.Combine(root, name + ".page.rgba"));
                VerifyPixels(rendered.Pixels.Span, expectedPage, 4, name + "/page");
                byte[] encodedPdf = await File.ReadAllBytesAsync(Path.Combine(root, name + ".pdf"));
                MediaBufferAllocator allocator = new(16 * 1024 * 1024);
                PdfImageDecoder decoder = new(renderOptions: new() { Dpi = 72, Supersampling = 1 });
                using (VideoFrame frame = await decoder.DecodeAsync(encodedPdf, new ImageDecodeRequest(), allocator))
                {
                    VerifyPixels(frame.Data.Span, expectedPage, 4, name + "/registered");
                }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        internal static async Task VerifyInvalidAsync()
        {
            byte[] jpeg = JpegTestData.Image();
            foreach (string entries in new[]
            {
                "/Width 2 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB",
                "/Width 1 /Height 1 /BitsPerComponent 1 /ColorSpace /DeviceGray",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Decode [0]",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Decode /Invalid",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Mask [0]",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Mask [-1 255]",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Mask [0 256]",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Mask [0.5 255]",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Mask /Invalid",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Mask << >>",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /DecodeParms << /ColorTransform 2 >>",
                "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /DecodeParms << /ColorTransform /Invalid >>"
            })
            {
                await RejectAsync<InvalidDataException>(jpeg, entries);
            }
            await RejectAsync<NotSupportedException>(jpeg, "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceCMYK");
            for (int length = 0; length < jpeg.Length; length++)
            {
                await RejectAsync<InvalidDataException>(jpeg[..length]);
            }
        }

        internal static async Task VerifyLimitsAndReuseAsync()
        {
            byte[] jpeg = JpegTestData.Image();
            byte[] image = PdfTestDocument.Stream(jpeg, "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Filter /DCTDecode");
            byte[] pdf = PdfTestDocument.Create("/I Do /I Do", resources: "/XObject << /I 5 0 R >>", extraObjects: image);
            using PdfDocument exact = await PdfReader.ReadAsync(new MemoryStream(pdf), new() { MaximumImageBytes = 68 });
            PdfPage page = exact.GetPage(1);
            Require(page.Images.Count == 1 && page.Images[0].Pixels.Span.SequenceEqual(new byte[] { 128, 128, 128, 255 }));
            await RejectAsync<MediaMemoryLimitException>(jpeg, options: new() { MaximumImageBytes = 67 });
            await RejectAsync<MediaMemoryLimitException>(jpeg, options: new() { MaximumImageBytes = 4 });
            using CancellationTokenSource cancellation = new();
            using PdfDocument cancelled = await PdfReader.ReadAsync(new MemoryStream(pdf), cancellationToken: cancellation.Token);
            await cancellation.CancelAsync();
            bool rejected = false;
            try { cancelled.GetPage(1, cancellation.Token); }
            catch (OperationCanceledException) { rejected = true; }
            Require(rejected);
        }

        internal static async Task VerifyAllocationAsync(string root)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(Path.Combine(root, "server-512.pdf"));
            document.GetPage(1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            PdfPage page = document.GetPage(1);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(page.Images.Single().Pixels.Length == 512 * 512 * 4 && allocated < 2 * 512 * 512 * 4);
        }

        internal static async Task VerifyMaskReferencesAsync()
        {
            byte[] jpeg = JpegTestData.Image();
            foreach (bool explicitImage in new[] { false, true })
            {
                byte[] image = PdfTestDocument.Stream(jpeg, "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Filter /DCTDecode /Decode [1 0] /Mask 6 0 R");
                byte[] mask;
                if (explicitImage)
                {
                    mask = PdfTestDocument.Stream([128], "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 1 /ImageMask true");
                }
                else { mask = PdfTestDocument.Bytes("[128 128]"); }
                byte[] pdf = PdfTestDocument.Create("/I Do", resources: "/XObject << /I 5 0 R >>", extraObjects: [image, mask]);
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(pdf));
                Require(document.GetPage(1).Images.Single(value => value.Name == "I").Pixels.Span.SequenceEqual(new byte[] { 127, 127, 127, 0 }));
            }
        }

        private static async Task RejectAsync<T>(byte[] jpeg,
            string entries = "/Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray", PdfReaderOptions? options = null) where T : Exception
        {
            byte[] image = PdfTestDocument.Stream(jpeg, "/Type /XObject /Subtype /Image /Filter /DCTDecode " + entries);
            byte[] pdf = PdfTestDocument.Create("/I Do", resources: "/XObject << /I 5 0 R >>", extraObjects: image);
            try
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(pdf), options);
                document.GetPage(1);
            }
            catch (T) { return; }
            throw new InvalidDataException("PDF JPEG accepted an invalid dictionary, payload or budget.");
        }

        private static void VerifyPixels(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, int tolerance, string name)
        {
            Require(actual.Length == expected.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                if (Math.Abs(actual[i] - expected[i]) > ((i & 3) == 3 ? 0 : tolerance))
                {
                    throw new InvalidDataException($"PDF JPEG independent pixel mismatch: {name}, component {i}, actual {actual[i]}, expected {expected[i]}.");
                }
            }
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("PDF JPEG dimensions, ownership or image reuse differs."); }
        }
    }
}
