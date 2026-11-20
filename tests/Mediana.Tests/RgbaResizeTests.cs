// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class RgbaResizeTests
    {
        [Theory]
        [InlineData(ImageResizeFilter.Bilinear)]
        [InlineData(ImageResizeFilter.Bicubic)]
        [InlineData(ImageResizeFilter.Lanczos3)]
        public void ReductionsSuppressAliasingAndRespectLightAndAlpha(ImageResizeFilter filter)
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            byte[] pixels = new byte[128 * 128 * 4];
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    int offset = (y * 128 + x) * 4;
                    byte value = (byte)(((x + y) & 1) * 255);
                    pixels[offset] = value; pixels[offset + 1] = value; pixels[offset + 2] = value; pixels[offset + 3] = 255;
                }
            }
            using VideoFrame input = RgbaFrame.CopyFrom(pixels, 128, 128, VideoColorInfo.Srgb, allocator);
            foreach (ResizeColorSpace space in Enum.GetValues<ResizeColorSpace>())
            {
                using VideoFrame output = RgbaResize.Apply(input, 7, 5, allocator, new() { Filter = filter, ColorSpace = space });
                int expected = space == ResizeColorSpace.LinearSrgb ? 188 : 128;
                for (int y = 0; y < 5; y++)
                {
                    ReadOnlySpan<byte> row = output.GetRow(0, y);
                    for (int x = 0; x < 7; x++)
                    {
                        Assert.InRange(row[x * 4], expected - 1, expected + 1);
                        Assert.Equal(row[x * 4], row[x * 4 + 1]);
                        Assert.Equal(255, row[x * 4 + 3]);
                    }
                }
            }
            using VideoFrame edge = RgbaFrame.CopyFrom([255, 0, 0, 255, 0, 0, 255, 0], 2, 1, VideoColorInfo.Srgb, allocator);
            using VideoFrame mixed = RgbaResize.Apply(edge, 1, 1, allocator, new() { Filter = filter });
            Assert.Equal(new byte[] { 255, 0, 0, 128 }, mixed.GetRow(0, 0).ToArray());
        }

        [Theory]
        [InlineData(1, 37, 12, 1)]
        [InlineData(37, 1, 1, 12)]
        [InlineData(13, 17, 31, 7)]
        [InlineData(1, 1, 40, 50)]
        public void ConstantsAndTransparentEdgesRemainStable(int width, int height, int targetWidth, int targetHeight)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 32; pixels[i + 1] = 71; pixels[i + 2] = 243; pixels[i + 3] = 129; }
            using VideoFrame input = RgbaFrame.CopyFrom(pixels, width, height, VideoColorInfo.Srgb, allocator);
            foreach (ImageResizeFilter filter in Enum.GetValues<ImageResizeFilter>())
            {
                using VideoFrame output = RgbaResize.Apply(input, targetWidth, targetHeight, allocator, new() { Filter = filter });
                for (int y = 0; y < targetHeight; y++)
                {
                    for (int x = 0; x < targetWidth; x++) { Assert.Equal(pixels[..4], output.GetRow(0, y).Slice(x * 4, 4).ToArray()); }
                }
            }
        }

        [Fact]
        public void OwnershipBudgetCancellationAndMetadataArePreserved()
        {
            MediaBufferAllocator sourceBudget = new(256);
            VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, new(4, 3));
            using WritableMediaBuffer writable = sourceBudget.Allocate(32);
            writable.Memory.Span.Fill(128);
            using MediaBuffer storage = writable.Publish();
            MediaTimestamp timestamp = new(10, new(1, 25));
            using VideoFrame input = new(format, storage, [new(3, 12, 8, 2)], timestamp);
            for (int limit = 1; limit < 400; limit += 17)
            {
                MediaBufferAllocator budget = new(limit);
                Assert.Throws<MediaMemoryLimitException>(() => RgbaResize.Apply(input, 7, 5, budget));
                Assert.Equal(0, budget.AllocatedBytes);
            }
            MediaBufferAllocator sufficient = new(4096);
            using VideoFrame output = RgbaResize.Apply(input, 7, 5, sufficient);
            Assert.Equal(7 * 5 * 4, sufficient.AllocatedBytes);
            Assert.Equal(timestamp, output.Timestamp);
            Assert.Equal(format.SampleAspectRatio, output.Format.SampleAspectRatio);
            Assert.Equal(format.Color, output.Format.Color);
            Assert.Throws<OperationCanceledException>(() => RgbaResize.Apply(input, 3, 3, sufficient, cancellationToken: new(true)));
            using VideoFrame identity = RgbaResize.Apply(input, 2, 2, sufficient);
            input.Dispose(); storage.Dispose();
            Assert.Equal(128, identity.GetRow(0, 0)[0]);
            output.Dispose();
            Assert.Equal(0, sufficient.AllocatedBytes);
        }

        [Fact]
        public void UnknownColorRequiresExplicitEncodedFiltering()
        {
            MediaBufferAllocator allocator = new(4096);
            using VideoFrame input = RgbaFrame.CopyFrom([10, 20, 30, 255], 1, 1, new(), allocator);
            Assert.Throws<NotSupportedException>(() => RgbaResize.Apply(input, 2, 2, allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => RgbaResize.Apply(input, 2, 2, allocator, new() { Filter = (ImageResizeFilter)88 }));
            using VideoFrame output = RgbaResize.Apply(input, 2, 2, allocator, new() { ColorSpace = ResizeColorSpace.Encoded });
            Assert.Equal(input.Format.Color, output.Format.Color);
        }
    }
}
