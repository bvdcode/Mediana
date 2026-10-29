// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;
using Xunit;
namespace Mediana.Tests
{
    public class PdfTests
    {
        private static readonly PdfRenderOptions Exact = new() { Dpi = 72, Supersampling = 1 };
        [Fact]
        public async Task AuthoredPagesPreservePaintingClippingAlphaAndDetachedOwnership() => await PdfReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Pdf"));
        [Fact]
        public async Task ReadsCompressedObjectsFromCrossReferenceStreams() => await PdfStructureTestInput.VerifyAsync();
        [Fact]
        public async Task CropRotationAndUserUnitDefineVisibleGeometry()
        {
            byte[] bytes = PdfTestDocument.Create("1 0 0 rg 10 20 20 10 re f", "/CropBox [10 20 110 70] /Rotate 90 /UserUnit 2");
            using PdfDocument doc = await Read(bytes);
            Assert.Equal(new PdfPageInfo(1, 100, 200, 90), doc.GetPageInfo(1));
            PixelBuffer image = PdfRenderer.Render(doc.GetPage(1), Exact);
            Assert.Equal(new Rgba32(255, 0, 0), image[10, 10]);
            Assert.Equal(new Rgba32(255, 255, 255), image[50, 10]);
        }
        [Fact]
        public async Task FormsRetainTheirOwnTransformAndBoundingClip()
        {
            byte[] form = PdfTestDocument.Stream(PdfTestDocument.Bytes("0 0 1 rg -100 -100 300 300 re f"), "/Type /XObject /Subtype /Form /BBox [0 0 20 20] /Matrix [1 0 0 1 50 20]");
            using PdfDocument doc = await Read(PdfTestDocument.Create("/Model Do 1 0 0 rg 0 0 10 10 re f", resources: "/XObject << /Model 5 0 R >>", extraObjects: form));
            PixelBuffer image = PdfRenderer.Render(doc.GetPage(1), Exact);
            Assert.Equal(new Rgba32(0, 0, 255), image[55, 75]);
            Assert.Equal(new Rgba32(255, 255, 255), image[45, 75]);
            Assert.Equal(new Rgba32(255, 0, 0), image[5, 95]);
        }
        [Fact]
        public async Task PackedImagesAndStencilDecodeArraysProduceExpectedPixels()
        {
            byte[] mask = PdfTestDocument.Stream([0b01010101], "/Type /XObject /Subtype /Image /Width 8 /Height 1 /ImageMask true /Decode [1 0]");
            using PdfDocument doc = await Read(PdfTestDocument.Create("1 0 0 rg q 160 0 0 20 0 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>", extraObjects: mask));
            PdfPage page = doc.GetPage(1);
            Assert.Single(page.Images);
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            Assert.Equal(new Rgba32(255, 255, 255), image[10, 90]);
            Assert.Equal(new Rgba32(255, 0, 0), image[30, 90]);
        }
        [Fact]
        public async Task EvenOddAndEmptyClipPreserveHoles()
        {
            using PdfDocument doc = await Read(PdfTestDocument.Create("1 0 0 rg 10 10 80 80 re 30 30 40 40 re f* q 0 0 0 0 re W n 0 0 1 rg 0 0 200 100 re f Q"));
            PixelBuffer image = PdfRenderer.Render(doc.GetPage(1), Exact);
            Assert.Equal(new Rgba32(255, 0, 0), image[20, 20]);
            Assert.Equal(new Rgba32(255, 255, 255), image[50, 50]);
        }
        [Fact]
        public async Task UnsupportedPaintingCannotSilentlyProduceSuccessfulPreview()
        {
            using PdfDocument doc = await Read(PdfTestDocument.Create("/S sh"));
            PdfPage page = doc.GetPage(1);
            Assert.NotEmpty(page.UnsupportedFeatures);
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(page));
        }
        [Fact]
        public async Task LimitsCancellationMalformedInputAndBorrowedStreamsAreChecked()
        {
            byte[] data = PdfTestDocument.Create("0 0 m 10 10 l S");
            using MemoryStream stream = new(data);
            using PdfDocument doc = await PdfReader.ReadAsync(stream);
            Assert.True(stream.CanRead);
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetPage(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetPage(2));
            Assert.Throws<OperationCanceledException>(() => doc.GetPage(1, new(true)));
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderer.Render(doc.GetPage(1), new() { MaximumSamples = 1 }));
            await Assert.ThrowsAsync<InvalidDataException>(() => PdfReader.ReadAsync(new MemoryStream(data), new() { MaximumInputBytes = 10 }));
            await Assert.ThrowsAsync<InvalidDataException>(() => PdfReader.ReadAsync(new MemoryStream(data), new() { MaximumObjects = 2 }));
            await Assert.ThrowsAsync<InvalidDataException>(() => PdfReader.ReadAsync(new MemoryStream(data[..^30])));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfReader.ReadAsync(new MemoryStream(data), cancellationToken: new(true)));
            using PdfDocument limited = await PdfReader.ReadAsync(new MemoryStream(data), new() { MaximumOperations = 1 });
            Assert.Throws<InvalidDataException>(() => limited.GetPage(1));
            doc.Dispose();
            Assert.Throws<ObjectDisposedException>(() => doc.GetPage(1));
        }
        private static Task<PdfDocument> Read(byte[] bytes) => PdfReader.ReadAsync(new MemoryStream(bytes));
    }
}
