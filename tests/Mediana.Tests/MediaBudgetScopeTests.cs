// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Svg;
using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    public class MediaBudgetScopeTests
    {
        [Fact]
        public async Task InvalidPdfSamplesReleaseTheOperationImageCharge()
        {
            byte[] pdf = PdfTestDocument.Create("/I Do", resources: "/XObject << /I 5 0 R >>",
                extraObjects: PdfTestDocument.Stream([128], "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Decode [/Invalid 1]"));
            MediaBufferAllocator allocator = new(4);
            await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImageDecoder().DecodeAsync(pdf, new(), allocator).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FailedReservationRollsBackEveryScopeAndLeasesKeepTheCharge()
        {
            MediaBufferAllocator root = new(8);
            MediaBufferAllocator child = root.CreateScope(16).CreateScope(32);
            using WritableMediaBuffer writable = child.Allocate(6);
            using MediaBuffer first = writable.Publish();
            using MediaBuffer retained = first.Retain();
            Assert.Equal(6, root.AllocatedBytes);
            Assert.Throws<MediaMemoryLimitException>(() => child.Allocate(3));
            Assert.Equal(6, child.AllocatedBytes);
            first.Dispose();
            Assert.Equal(6, root.AllocatedBytes);
            retained.Dispose();
            Assert.Equal(0, root.AllocatedBytes);
            Assert.Equal(0, child.AllocatedBytes);
            Assert.True(child.IsWithin(root));
            Assert.False(root.IsWithin(child));
        }

        [Fact]
        public async Task NestedSvgImagesAndOutputConsumeTheSameBudget()
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddImageDecoder(new SvgImageDecoder(renderOptions: new() { Supersampling = 1 }))
                .AddImageDecoder(new EmbeddedTestImageDecoder()).Build();
            byte[] inner = EmbeddedImageTestInput.Svg("PIX"u8.ToArray());
            byte[] outer = EmbeddedImageTestInput.Svg(inner);
            MediaBufferAllocator small = new(7);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(() => engine.DecodeImageAsync(outer.AsMemory(), small).AsTask());
            Assert.Equal(0, small.AllocatedBytes);
            MediaBufferAllocator enough = new(8);
            using VideoFrame frame = await engine.DecodeImageAsync(outer.AsMemory(), enough);
            Assert.Equal(4, enough.AllocatedBytes);
            frame.Dispose();
            Assert.Equal(0, enough.AllocatedBytes);
        }

        [Fact]
        public async Task LocalSvgLimitAlsoAppliesInsideALargerOperation()
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddImageDecoder(new SvgImageDecoder(new() { MaximumImageBytes = 3 }))
                .AddImageDecoder(new EmbeddedTestImageDecoder()).Build();
            MediaBufferAllocator allocator = new(1024);
            byte[] encoded = EmbeddedImageTestInput.Svg("PIX"u8.ToArray());
            await Assert.ThrowsAsync<MediaMemoryLimitException>(() => engine.DecodeImageAsync(encoded.AsMemory(), allocator).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
