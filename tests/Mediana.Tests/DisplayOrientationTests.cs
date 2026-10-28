using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class DisplayOrientationTests
    {
        [Theory]
        [InlineData(1, 0, 0, 1, 3, 2, "123456")]
        [InlineData(0, 1, -1, 0, 2, 3, "415263")]
        [InlineData(-1, 0, 0, -1, 3, 2, "654321")]
        [InlineData(0, -1, 1, 0, 2, 3, "362514")]
        [InlineData(-1, 0, 0, 1, 3, 2, "321654")]
        [InlineData(1, 0, 0, -1, 3, 2, "456123")]
        [InlineData(0, 1, 1, 0, 2, 3, "142536")]
        [InlineData(0, -1, -1, 0, 2, 3, "635241")]
        public void AllEightOrientationsUseExactPixelsAndNormalizeCanvasTranslation(int a, int b, int c, int d,
            int expectedWidth, int expectedHeight, string order)
        {
            MediaBufferAllocator allocator = new(33 + 24);
            using VideoFrame input = Frame(allocator, SampleAspectRatio.Square);
            VideoDisplayMatrix matrix = new(a, b, 0, c, d, 0, 13.25, -29.5, 1);
            using VideoFrame output = DisplayResize.Fit(input, matrix, 10, 10, allocator);
            Assert.Equal(expectedWidth, output.Format.Width);
            Assert.Equal(expectedHeight, output.Format.Height);
            Assert.Equal(SampleAspectRatio.Square, output.Format.SampleAspectRatio);
            Assert.Equal(input.Format.Color, output.Format.Color);
            Assert.Equal(input.Timestamp, output.Timestamp);
            Assert.Equal(input.Duration, output.Duration);
            for (int index = 0; index < order.Length; index++)
            {
                byte value = (byte)(order[index] - '0');
                Assert.Equal(new byte[] { value, (byte)(value + 10), (byte)(value + 20), (byte)(255 - value) },
                    output.Data.Slice(index * 4, 4).ToArray());
            }
            input.Dispose();
            Assert.Equal(24, allocator.AllocatedBytes);
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(100, 100, 2, 6)]
        [InlineData(2, 4, 1, 4)]
        [InlineData(1, 4, 1, 3)]
        public void AspectCorrectionPrecedesRotationAndBoundingUsesTheRotatedAxes(int maximumWidth, int maximumHeight,
            int expectedWidth, int expectedHeight)
        {
            MediaBufferAllocator allocator = new(256);
            using VideoFrame input = Frame(allocator, new SampleAspectRatio(2, 1));
            VideoDisplayMatrix clockwise = new(0, 1, 0, -1, 0, 0, 0, 0, 1);
            using VideoFrame output = DisplayResize.Fit(input, clockwise, maximumWidth, maximumHeight, allocator);
            Assert.Equal(expectedWidth, output.Format.Width);
            Assert.Equal(expectedHeight, output.Format.Height);
            Assert.Equal(SampleAspectRatio.Square, output.Format.SampleAspectRatio);
            Assert.Equal(new SampleAspectRatio(2, 1), input.Format.SampleAspectRatio);
        }

        [Theory]
        [InlineData(2, 0, 0, 1, 0, 1)]
        [InlineData(1, 1, 0, 1, 0, 1)]
        [InlineData(0, 0, 0, 0, 0, 1)]
        [InlineData(1, 0, 0, 1, 0.25, 1)]
        [InlineData(1, 0, 0, 1, 0, 0.5)]
        [InlineData(0.99998, 0, 0, 1, 0, 1)]
        public void ScalingShearDegeneratePerspectiveAndApproximateRotationFailWithoutAllocation(double a, double b,
            double c, double d, double perspective, double w)
        {
            MediaBufferAllocator allocator = new(33);
            using VideoFrame input = Frame(allocator, SampleAspectRatio.Square);
            VideoDisplayMatrix matrix = new(a, b, perspective, c, d, 0, 0, 0, w);
            Assert.Throws<NotSupportedException>(() => DisplayResize.Fit(input, matrix, 10, 10, allocator));
            Assert.Equal(33, allocator.AllocatedBytes);
        }

        [Fact]
        public void InvalidMatrixUnknownAspectAndCancellationLeaveStorageUnchanged()
        {
            MediaBufferAllocator allocator = new(128);
            using VideoFrame input = Frame(allocator, null);
            Assert.Throws<NotSupportedException>(() => DisplayResize.Fit(input, VideoDisplayMatrix.Identity, 10, 10, allocator));
            Assert.Throws<ArgumentException>(() => DisplayResize.Fit(input, SampleAspectRatio.Square,
                VideoDisplayMatrix.Identity with { M31 = double.PositiveInfinity }, 10, 10, allocator));
            Assert.Throws<OperationCanceledException>(() => DisplayResize.Fit(input, SampleAspectRatio.Square,
                VideoDisplayMatrix.Identity, 10, 10, allocator, new CancellationToken(true)));
            using VideoFrame output = DisplayResize.Fit(input, SampleAspectRatio.Square, VideoDisplayMatrix.Identity, 10, 10, allocator);
            Assert.Null(input.Format.SampleAspectRatio);
            Assert.Equal(SampleAspectRatio.Square, output.Format.SampleAspectRatio);
        }

        private static VideoFrame Frame(MediaBufferAllocator allocator, SampleAspectRatio? aspectRatio)
        {
            VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Rgb, ColorRange.Full);
            VideoFormat format = new(3, 2, VideoPixelFormat.Rgba32, color, aspectRatio);
            using WritableMediaBuffer writable = allocator.Allocate(33);
            writable.Memory.Span.Fill(255);
            for (int index = 0; index < 6; index++)
            {
                byte value = (byte)(index + 1);
                int offset = 1 + index / 3 * 16 + index % 3 * 4;
                new byte[] { value, (byte)(value + 10), (byte)(value + 20), (byte)(255 - value) }.CopyTo(writable.Memory.Slice(offset, 4));
            }
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, [new PlaneLayout(1, 16, 12, 2)],
                new MediaTimestamp(7, new TimeBase(1, 30)), new MediaDuration(1, new TimeBase(1, 30)));
        }
    }
}
