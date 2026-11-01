// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PixelBufferOwnershipTests
    {
        [Fact]
        public void PublicationTransfersTheSameStorageAndRetainedFramesKeepTheBudget()
        {
            MediaBufferAllocator allocator = new(8);
            using PixelBuffer pixels = new(2, 1, allocator);
            pixels[0, 0] = new(10, 20, 30, 40);
            ReadOnlyMemory<byte> storage = pixels.Pixels;
            MediaTimestamp time = new(3, new TimeBase(1, 24));
            MediaDuration duration = new(1, time.TimeBase);
            using VideoFrame original = pixels.PublishVideoFrame(timestamp: time, duration: duration);
            Assert.True(storage.Equals(original.Data));
            Assert.Throws<ObjectDisposedException>(() => pixels.Pixels);
            Assert.Throws<ObjectDisposedException>(() => pixels[0, 0]);
            Assert.Throws<ObjectDisposedException>(() => pixels.Clear(default));
            Assert.Throws<ObjectDisposedException>(() => pixels.PublishVideoFrame());
            pixels.Dispose();
            Assert.Equal(time, original.Timestamp);
            Assert.Equal(duration, original.Duration);
            Assert.Equal(VideoColorInfo.Srgb, original.Format.Color);
            Assert.Equal(SampleAspectRatio.Square, original.Format.SampleAspectRatio);
            using VideoFrame retained = original.Retain();
            original.Dispose();
            Assert.Equal(new byte[] { 10, 20, 30, 40 }, retained.GetRow(0, 0)[..4].ToArray());
            Assert.Equal(8, allocator.AllocatedBytes);
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CancellationPreservesWritableOwnershipAndDisposalReleasesUnpublishedPixels()
        {
            MediaBufferAllocator allocator = new(4);
            using (PixelBuffer pixels = new(1, 1, allocator))
            {
                Assert.Equal(new byte[4], pixels.Pixels.ToArray());
                Assert.Throws<OperationCanceledException>(() => pixels.PublishVideoFrame(new CancellationToken(true)));
                pixels[0, 0] = new(1, 2, 3, 4);
                Assert.Equal(new Rgba32(1, 2, 3, 4), pixels[0, 0]);
                Assert.Equal(4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<MediaMemoryLimitException>(() => new PixelBuffer(2, 1, allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(int.MaxValue, int.MaxValue, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ExplicitCopyRemainsIndependentAndBothOwnersReleaseTheirBudgets()
        {
            MediaBufferAllocator allocator = new(8);
            using PixelBuffer pixels = new(1, 1, allocator);
            pixels[0, 0] = new(1, 2, 3, 4);
            using VideoFrame copy = pixels.ToVideoFrame(allocator);
            Assert.False(((ReadOnlyMemory<byte>)pixels.Pixels).Equals(copy.Data));
            pixels.Clear(default);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, copy.Data.ToArray());
            pixels.Dispose();
            Assert.Equal(4, allocator.AllocatedBytes);
            copy.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void SupersampledPixelsCanBeWrittenDirectlyToBudgetedOutput(int factor)
        {
            MediaBufferAllocator allocator = new(8);
            using PixelBuffer samples = new(2 * factor, factor);
            samples.Clear(new(100, 50, 200, 128));
            using PixelBuffer output = new(2, 1, allocator);
            samples.DownsampleTo(output, factor);
            using VideoFrame frame = output.PublishVideoFrame();
            Assert.Equal(new byte[] { 100, 50, 200, 128, 100, 50, 200, 128 }, frame.Data.ToArray());
            Assert.Equal(8, allocator.AllocatedBytes);
        }
    }
}
