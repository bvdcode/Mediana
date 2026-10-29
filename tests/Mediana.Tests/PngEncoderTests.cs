// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class PngEncoderTests
    {
        private static readonly VideoColorInfo Srgb = new(ColorPrimaries.Bt709, TransferFunction.Srgb,
            MatrixCoefficients.Rgb, ColorRange.Full, ChromaLocation.Unspecified);

        [Fact]
        public void EncodesPaddedStraightRgbaWithColorAspectAndIndependentOwnership()
        {
            MediaBufferAllocator allocator = new(4096);
            byte[] firstRow = [255, 0, 0, 0, 0, 255, 0, 127];
            byte[] secondRow = [0, 0, 255, 128, 255, 255, 255, 255];
            using WritableMediaBuffer writable = allocator.Allocate(24);
            firstRow.CopyTo(writable.Memory.Slice(1, 8));
            secondRow.CopyTo(writable.Memory.Slice(13, 8));
            using MediaBuffer pixels = writable.Publish();
            VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32, Srgb, new SampleAspectRatio(4, 3));
            using VideoFrame frame = new(format, pixels, [new PlaneLayout(1, 12, 8, 2)]);
            using MediaBuffer encoded = PngEncoder.Encode(frame, allocator);
            frame.Dispose();
            pixels.Dispose();
            Dictionary<string, byte[]> chunks = PngTestInput.ReadChunks(encoded.Memory.Span);
            Assert.Equal(new byte[] { 0, 0, 0, 2, 0, 0, 0, 2, 8, 6, 0, 0, 0 }, chunks["IHDR"]);
            Assert.Equal(new byte[] { 0 }, chunks["sRGB"]);
            Assert.Equal(new byte[] { 0, 0, 0, 3, 0, 0, 0, 4, 0 }, chunks["pHYs"]);
            Assert.Empty(chunks["IEND"]);
            byte[] filtered = PngTestInput.Inflate(chunks["IDAT"]);
            Assert.Equal(new byte[] { 0, 255, 0, 0, 0, 0, 255, 0, 127, 0, 0, 0, 255, 128, 255, 255, 255, 255 }, filtered);
            Assert.True(allocator.AllocatedBytes >= encoded.Memory.Length);
            encoded.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 20000)]
        [InlineData(1024, 64)]
        [InlineData(17000, 2)]
        public void IncompressiblePixelsAcrossRowAndCompressionBlockBoundariesRoundtripExactly(int width, int height)
        {
            byte[] pixels = new byte[width * height * 4];
            new Random(721).NextBytes(pixels);
            MediaBufferAllocator allocator = new(pixels.Length * 3L + height * 2L + 1024);
            using VideoFrame frame = Frame(allocator, new VideoFormat(width, height, VideoPixelFormat.Rgba32, Srgb, SampleAspectRatio.Square), pixels);
            using MediaBuffer encoded = PngEncoder.Encode(frame, allocator);
            Dictionary<string, byte[]> chunks = PngTestInput.ReadChunks(encoded.Memory.Span);
            byte[] filtered = PngTestInput.Inflate(chunks["IDAT"]);
            Assert.Equal(pixels.Length + height, filtered.Length);
            for (int row = 0; row < height; row++)
            {
                Assert.Equal(0, filtered[row * (width * 4 + 1)]);
                Assert.True(pixels.AsSpan(row * width * 4, width * 4).SequenceEqual(filtered.AsSpan(row * (width * 4 + 1) + 1, width * 4)));
            }
            Assert.Equal(new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, chunks["pHYs"]);
        }

        [Fact]
        public void UnsupportedColorOrAspectCancellationAndBudgetFailurePreserveTheInput()
        {
            MediaBufferAllocator allocator = new(8);
            using VideoFrame input = Frame(allocator, new VideoFormat(1, 1, VideoPixelFormat.Rgba32, Srgb, SampleAspectRatio.Square), [1, 2, 3, 4]);
            Assert.Throws<OperationCanceledException>(() => PngEncoder.Encode(input, allocator, new CancellationToken(true)));
            Assert.Throws<MediaMemoryLimitException>(() => PngEncoder.Encode(input, allocator));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, input.Data.ToArray());
            using VideoFrame unknownAspect = Frame(allocator, new VideoFormat(1, 1, VideoPixelFormat.Rgba32, Srgb), [1, 2, 3, 4]);
            Assert.Throws<NotSupportedException>(() => PngEncoder.Encode(unknownAspect, allocator));
            unknownAspect.Dispose();
            using VideoFrame unknownColor = Frame(allocator, new VideoFormat(1, 1, VideoPixelFormat.Rgba32, sampleAspectRatio: SampleAspectRatio.Square), [1, 2, 3, 4]);
            Assert.Throws<NotSupportedException>(() => PngEncoder.Encode(unknownColor, allocator));
            Assert.Equal(8, allocator.AllocatedBytes);
        }

        [Fact]
        public void FailedReadingOfDisposedInputReleasesTheReservedOutput()
        {
            MediaBufferAllocator allocator = new(1024);
            VideoFrame input = Frame(allocator, new VideoFormat(1, 1, VideoPixelFormat.Rgba32, Srgb, SampleAspectRatio.Square), [1, 2, 3, 4]);
            input.Dispose();
            Assert.Throws<ObjectDisposedException>(() => PngEncoder.Encode(input, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static VideoFrame Frame(MediaBufferAllocator allocator, VideoFormat format, byte[] pixels)
        {
            using WritableMediaBuffer writable = allocator.Allocate(pixels.Length);
            pixels.CopyTo(writable.Memory);
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, format.CreateTightLayout());
        }

    }
}
