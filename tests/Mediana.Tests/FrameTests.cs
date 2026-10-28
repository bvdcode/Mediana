using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class FrameTests
    {
        [Fact]
        public void ResizeHonorsStrideAndPreservesTimeAndColor()
        {
            MediaBufferAllocator allocator = new(128);
            VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32,
                new VideoColorInfo(Transfer: TransferFunction.Srgb, Range: ColorRange.Full), new SampleAspectRatio(4, 3));
            using WritableMediaBuffer writable = allocator.Allocate(24);
            byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255];
            pixels.CopyTo(writable.Memory.Span);
            byte[] secondRow = [7, 8, 9, 255, 10, 11, 12, 255];
            secondRow.CopyTo(writable.Memory.Span[12..]);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame frame = new(format, buffer, [new PlaneLayout(0, 12, 8, 2)],
                new MediaTimestamp(7, new TimeBase(1, 30)));
            using VideoFrame resized = NearestNeighborResize.Apply(frame, 1, 2, allocator);
            Assert.Equal(new byte[] { 1, 2, 3, 255, 7, 8, 9, 255 }, resized.Data.ToArray());
            Assert.Equal(frame.Timestamp, resized.Timestamp);
            Assert.Equal(format.Color, resized.Format.Color);
            Assert.Equal(format.SampleAspectRatio, resized.Format.SampleAspectRatio);
        }

        [Fact]
        public void PlanarOddDimensionsAreRoundedUpAndBoundsAreChecked()
        {
            VideoFormat format = new(3, 3, VideoPixelFormat.Yuv420P8);
            PlaneLayout[] layout = format.CreateTightLayout();
            Assert.Equal(new PlaneLayout(9, 2, 2, 2), layout[1]);
            Assert.Equal(17, layout[2].EndOffset);
            MediaBufferAllocator allocator = new(16);
            using WritableMediaBuffer writable = allocator.Allocate(16);
            using MediaBuffer buffer = writable.Publish();
            Assert.Throws<ArgumentException>(() => new VideoFrame(format, buffer, layout));
            Assert.Equal(16, allocator.AllocatedBytes);
        }

        [Fact]
        public void FailedOrCancelledResizeDoesNotLeakBuffers()
        {
            MediaBufferAllocator allocator = new(4);
            using VideoFrame frame = FrameFixture.Create(allocator, 9);
            Assert.Throws<MediaMemoryLimitException>(() => NearestNeighborResize.Apply(frame, 2, 2, allocator));
            Assert.Throws<OperationCanceledException>(() =>
                NearestNeighborResize.Apply(frame, 1, 1, allocator, new CancellationToken(true)));
            Assert.Equal(4, allocator.AllocatedBytes);
            using VideoFrame retained = frame.Retain();
            frame.Dispose();
            Assert.Equal(9, retained.Data.Span[0]);
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void OversizedLayoutFailsBeforeAllocation()
        {
            VideoFormat format = new(int.MaxValue, int.MaxValue, VideoPixelFormat.Rgba32);
            Assert.Throws<OverflowException>(() => format.CreateTightLayout());
        }
    }
}
