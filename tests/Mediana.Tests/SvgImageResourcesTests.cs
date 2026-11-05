// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Drawing;
using Mediana.Images.Svg;

namespace Mediana.Tests
{
    public class SvgImageResourcesTests
    {
        [Fact]
        public void EmbeddedImagesUseOneDecodedFrameAndReleaseOnSceneDisposal() => SvgReferenceTestInput.VerifyEmbeddedImages();

        [Fact]
        public void BorrowedResolverPixelsAreOwnedOnceAndFailuresReleasePriorImages()
        {
            using PixelBuffer source = new(2, 1);
            source.Clear(new(255, 0, 0));
            MediaBufferAllocator images = new(8);
            SvgReaderOptions options = new() { ImageResolver = _ => source, ImageAllocator = images };
            using SvgDocument document = SvgReader.Read("<svg width='2' height='1'><image href='one' width='2' height='1'/></svg>", options);
            Assert.Equal(8, images.AllocatedBytes);
            source.Clear(new(0, 0, 255));
            using PixelBuffer output = SvgRenderer.Render(document, new() { Supersampling = 1 });
            Assert.Equal(new Rgba32(255, 0, 0), output[0, 0]);
            document.Dispose();
            Assert.Equal(0, images.AllocatedBytes);
            Assert.Throws<ObjectDisposedException>(() => SvgRenderer.Render(document));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read("<svg><image href='one'/><image href='one' width='-1'/></svg>", options));
            Assert.Equal(0, images.AllocatedBytes);
            Assert.Throws<MediaMemoryLimitException>(() => SvgReader.Read("<svg><image href='one'/><image href='two'/></svg>", options));
            Assert.Equal(0, images.AllocatedBytes);
        }

        [Fact]
        public void SupersamplingSharesTheOutputBudgetAndReleasesOnFailure()
        {
            using SvgDocument document = SvgReader.Read("<svg width='2' height='2'><rect width='2' height='2'/></svg>");
            MediaBufferAllocator allocator = new(80);
            using (PixelBuffer output = SvgRenderer.Render(document, new() { Supersampling = 2 }, allocator: allocator))
            { Assert.Equal(16, allocator.AllocatedBytes); }
            Assert.Equal(0, allocator.AllocatedBytes);
            MediaBufferAllocator insufficient = new(79);
            Assert.Throws<MediaMemoryLimitException>(() => SvgRenderer.Render(document, new() { Supersampling = 2 }, allocator: insufficient));
            Assert.Equal(0, insufficient.AllocatedBytes);
        }
    }
}
