// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Xunit;
namespace Mediana.Tests
{
    public class PdfImageTests
    {
        [Fact]
        public async Task PngPredictionAndColorKeyPreserveEveryImageSample()
        {
            byte[] predicted = [1, 10, 20, 30, 30, 30, 30];
            using MemoryStream encoded = new();
            using (ZLibStream zip = new(encoded, CompressionLevel.SmallestSize, true)) { zip.Write(predicted); }
            byte[] image = PdfTestDocument.Stream(encoded.ToArray(), "/Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /Columns 2 >> /Mask [10 10 20 20 30 30]");
            using PdfDocument document = await Read(image);
            PdfImage pixels = Assert.Single(document.GetPage(1).Images);
            Assert.Equal(new byte[] { 10, 20, 30, 0, 40, 50, 60, 255 }, pixels.Pixels.ToArray());
        }
        [Fact]
        public async Task ImageMasksAndIndexedPalettesRetainTheirOwnPrecision()
        {
            byte[] image = PdfTestDocument.Stream([0b00011011], "/Type /XObject /Subtype /Image /Width 4 /Height 1 /BitsPerComponent 2 /ColorSpace [/Indexed /DeviceRGB 3 <FF000000FF000000FFFFFFFF>]");
            using PdfDocument document = await Read(image);
            Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 }, document.GetPage(1).Images[0].Pixels.ToArray());
        }
        [Fact]
        public async Task SoftMasksCombineAlphaAndRetainedFramesAreBudgeted()
        {
            byte[] image = PdfTestDocument.Stream([255, 0, 0, 0, 0, 255], "/Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB /SMask 6 0 R");
            byte[] mask = PdfTestDocument.Stream([128, 255], "/Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray");
            using PdfDocument document = await Read(image, mask);
            PdfPage page = document.GetPage(1);
            PdfImage pixels = page.Images.Single(x => x.Name == "I");
            Assert.Equal(new byte[] { 255, 0, 0, 128, 0, 0, 255, 255 }, pixels.Pixels.ToArray());
            document.Dispose();
            MediaBufferAllocator allocator = new(8);
            using (VideoFrame frame = pixels.ToVideoFrame(allocator))
            {
                Assert.Equal(pixels.Pixels.ToArray(), frame.Data.ToArray());
                Assert.Equal(8, allocator.AllocatedBytes);
                Assert.Throws<MediaMemoryLimitException>(() => pixels.ToVideoFrame(allocator));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
        [Fact]
        public async Task Jp2ImageUsesDeclaredColorAndPreservesIndependentRgbReference()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg2000");
            await PdfReferenceTestInput.VerifyJp2Async(root);
        }
        [Fact]
        public async Task ReusedStencilUsesTheInkOfEachInvocation()
        {
            byte[] image = PdfTestDocument.Stream([0], "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ImageMask true");
            byte[] bytes = PdfTestDocument.Create("1 0 0 rg q 10 0 0 10 0 0 cm /I Do Q 0 0 1 rg q 10 0 0 10 20 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>", extraObjects: image);
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(bytes));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, document.GetPage(1).Images[0].Pixels.ToArray());
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, document.GetPage(1).Images[1].Pixels.ToArray());
        }
        private static Task<PdfDocument> Read(params byte[][] objects) => PdfReader.ReadAsync(new MemoryStream(PdfTestDocument.Create("q 100 0 0 50 0 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>", extraObjects: objects)));
    }
}
