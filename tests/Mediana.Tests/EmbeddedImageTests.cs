// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Images.Svg;

namespace Mediana.Tests
{
    public class EmbeddedImageTests
    {
        [Fact]
        public Task EngineRegistrationReachesWordAndAsynchronousSvgImages() => EmbeddedImageTestInput.VerifyRegistryAsync();
        [Fact]
        public Task LinearPngUsesTheSameSrgbPreparationInsideSvg() => EmbeddedImageTestInput.VerifyColorAsync();
        [Fact]
        public Task JpegWorksInsideSvgThroughSharedDecoders() => EmbeddedImageTestInput.VerifyJpegAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));

        [Fact]
        public async Task RecursiveResourcesConsumeTheSameDepthLimit()
        {
            ImageDecodeContext context = new([new EmbeddedTestImageDecoder(recursive: true)], maximumDepth: 3);
            MediaBufferAllocator allocator = new(4);
            await Assert.ThrowsAsync<InvalidDataException>(() => context.DecodeAsync("PIX"u8.ToArray(), new(), allocator).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationAfterDecodeReleasesTheReturnedFrame()
        {
            using CancellationTokenSource cancellation = new();
            ImageDecodeContext context = new([new EmbeddedTestImageDecoder(decoded: cancellation.Cancel)]);
            MediaBufferAllocator allocator = new(4);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.DecodeAsync("PIX"u8.ToArray(), new(), allocator, cancellation.Token).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void SynchronousReaderDoesNotStartAnAsynchronousOnlyDecoder()
        {
            SvgReaderOptions options = new() { Images = new([new EmbeddedTestImageDecoder()]) };
            Assert.Throws<NotSupportedException>(() => SvgReader.Read(EmbeddedImageTestInput.Svg("PIX"u8.ToArray()), options));
        }
    }
}
