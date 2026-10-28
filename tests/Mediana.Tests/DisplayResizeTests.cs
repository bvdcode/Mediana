using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class DisplayResizeTests
    {
        [Theory]
        [InlineData(94, 62, 4, 3, 1000, 1000, 125, 62)]
        [InlineData(94, 62, 4, 3, 100, 100, 100, 49)]
        [InlineData(94, 62, 4, 3, 200, 31, 63, 31)]
        [InlineData(8, 6, 1, 2, 100, 100, 4, 6)]
        [InlineData(8, 6, 1, 1, 100, 100, 8, 6)]
        [InlineData(8, 6, int.MaxValue, 1, 32, 32, 32, 1)]
        [InlineData(8, 6, 1, int.MaxValue, 32, 32, 1, 6)]
        public void FitsDisplayAspectWithoutExceedingTheBoxOrEnlargingDisplayHeight(int width, int height,
            int numerator, int denominator, int maximumWidth, int maximumHeight, int expectedWidth, int expectedHeight)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, sampleAspectRatio: new SampleAspectRatio(numerator, denominator));
            using WritableMediaBuffer writable = allocator.Allocate(width * height * 4);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame input = new(format, buffer, format.CreateTightLayout());
            using VideoFrame output = DisplayResize.Fit(input, maximumWidth, maximumHeight, allocator);
            Assert.Equal(expectedWidth, output.Format.Width);
            Assert.Equal(expectedHeight, output.Format.Height);
            Assert.Equal(SampleAspectRatio.Square, output.Format.SampleAspectRatio);
            Assert.Equal(format.SampleAspectRatio, input.Format.SampleAspectRatio);
        }

        [Fact]
        public void SamplesPaddedStorageAndPreservesTimeColorAndIndependentOwnership()
        {
            MediaBufferAllocator allocator = new(256);
            VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Rgb, ColorRange.Full);
            VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32, color, new SampleAspectRatio(2, 1));
            MediaTimestamp timestamp = new(123, new TimeBase(1, 30));
            MediaDuration duration = new(1, new TimeBase(1, 30));
            using WritableMediaBuffer writable = allocator.Allocate(24);
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }.CopyTo(writable.Memory.Slice(1, 8));
            new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 }.CopyTo(writable.Memory.Slice(13, 8));
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame input = new(format, buffer, [new PlaneLayout(1, 12, 8, 2)], timestamp, duration);
            using VideoFrame output = DisplayResize.Fit(input, 4, 2, allocator);
            input.Dispose();
            buffer.Dispose();
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2, 3, 4, 5, 6, 7, 8, 5, 6, 7, 8 }, output.GetRow(0, 0).ToArray());
            Assert.Equal(new byte[] { 9, 10, 11, 12, 9, 10, 11, 12, 13, 14, 15, 16, 13, 14, 15, 16 }, output.GetRow(0, 1).ToArray());
            Assert.Equal(color, output.Format.Color);
            Assert.Equal(timestamp, output.Timestamp);
            Assert.Equal(duration, output.Duration);
            Assert.Equal(32, allocator.AllocatedBytes);
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void UnknownAspectRequiresAnExplicitInterpretationAndFailuresDoNotAllocate()
        {
            MediaBufferAllocator allocator = new(8);
            VideoFormat format = new(1, 1, VideoPixelFormat.Rgba32);
            using WritableMediaBuffer writable = allocator.Allocate(4);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame input = new(format, buffer, format.CreateTightLayout());
            Assert.Throws<NotSupportedException>(() => DisplayResize.Fit(input, 1, 1, allocator));
            Assert.Throws<ArgumentException>(() => DisplayResize.Fit(input, default(SampleAspectRatio), 1, 1, allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => DisplayResize.Fit(input, SampleAspectRatio.Square, 0, 1, allocator));
            Assert.Throws<OperationCanceledException>(() => DisplayResize.Fit(input, SampleAspectRatio.Square, 1, 1, allocator, new CancellationToken(true)));
            using VideoFrame output = DisplayResize.Fit(input, SampleAspectRatio.Square, 1, 1, allocator);
            Assert.Equal(SampleAspectRatio.Square, output.Format.SampleAspectRatio);
            Assert.Null(input.Format.SampleAspectRatio);
            Assert.Equal(8, allocator.AllocatedBytes);
        }
    }
}
