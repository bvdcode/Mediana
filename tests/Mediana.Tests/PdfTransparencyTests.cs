// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PdfTransparencyTests
    {
        [Fact]
        public Task GroupOpacityIsAppliedOnce() => PdfNonPaintingAnnotationTestInput.VerifyGroupOpacityAsync();

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public Task AlphaLuminosityAndTransferMasksComposeCorrectly(bool luminosity, bool invert) =>
            PdfNonPaintingAnnotationTestInput.VerifySoftMaskAsync(luminosity, invert);

        [Fact]
        public Task CachedMasksAndLayersRespectTheBudgetAndReleaseTheirStorage() => PdfNonPaintingAnnotationTestInput.VerifyMaskBudgetAsync();

        [Fact]
        public async Task MaskCoordinatesAreCapturedWhenTheGraphicsStateIsSet()
        {
            const string content = "1 0 0 1 20 0 cm /M gs 1 0 0 1 -20 0 cm 1 0 0 rg 0 0 200 100 re f";
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfNonPaintingAnnotationTestInput.SoftMask(false, page: content)));
            using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.Equal(new Rgba32(255, 255, 255), image[10, 50]);
            Assert.Equal(new Rgba32(255, 0, 0), image[40, 50]);
            Assert.Equal(new Rgba32(255, 255, 255), image[130, 50]);
        }

        [Fact]
        public async Task SupersamplingUsesTheSameRasterBudgetAsOutput()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfTestDocument.Create("")));
            MediaBufferAllocator allocator = new(80000);
            Assert.Throws<MediaMemoryLimitException>(() => PdfRenderer.Render(document, 1,
                new() { Dpi = 72, Supersampling = 2 }, allocator: allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
