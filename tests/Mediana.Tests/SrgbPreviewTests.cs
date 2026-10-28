using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class SrgbPreviewTests
    {
        private static readonly VideoColorInfo Srgb = new(ColorPrimaries.Bt709, TransferFunction.Srgb,
            MatrixCoefficients.Rgb, ColorRange.Full);

        [Theory]
        [InlineData(1, 0, 0, 1)]
        [InlineData(0, 1, -1, 0)]
        [InlineData(-1, 0, 0, -1)]
        [InlineData(0, -1, 1, 0)]
        [InlineData(-1, 0, 0, 1)]
        [InlineData(1, 0, 0, -1)]
        [InlineData(0, 1, 1, 0)]
        [InlineData(0, -1, -1, 0)]
        public void AllOrientationsMatchSeparateColorAndGeometryForOddPaddedYuv(int a, int b, int c, int d)
        {
            VideoDisplayMatrix matrix = new(a, b, 0, c, d, 0, 13.25, -29.5, 1);
            ChromaLocation[] locations = [ChromaLocation.Left, ChromaLocation.Center, ChromaLocation.TopLeft,
                ChromaLocation.Top, ChromaLocation.BottomLeft, ChromaLocation.Bottom];
            foreach (ChromaLocation location in locations)
            {
                MediaBufferAllocator allocator = new(4096);
                VideoColorInfo color = new(ColorPrimaries.Bt2020, TransferFunction.Bt709,
                    MatrixCoefficients.Bt2020NonConstant, ColorRange.Limited, location);
                VideoFormat format = new(5, 3, VideoPixelFormat.Yuv420P8, color, new SampleAspectRatio(4, 3));
                using VideoFrame input = Frame(allocator, format, 80,
                    [new PlaneLayout(1, 8, 5, 3), new PlaneLayout(32, 5, 3, 2), new PlaneLayout(50, 6, 3, 2)]);
                using VideoFrame converted = SrgbConverter.Apply(input, allocator);
                using VideoFrame expected = DisplayResize.Fit(converted, matrix, 4, 3, allocator);
                using VideoFrame actual = SrgbPreview.Fit(input, matrix, 4, 3, allocator);
                Assert.Equal(expected.Format.Width, actual.Format.Width);
                Assert.Equal(expected.Format.Height, actual.Format.Height);
                Assert.Equal(expected.Data.ToArray(), actual.Data.ToArray());
                Assert.Equal(Srgb, actual.Format.Color);
                Assert.Equal(SampleAspectRatio.Square, actual.Format.SampleAspectRatio);
                Assert.Equal(input.Timestamp, actual.Timestamp);
                Assert.Equal(input.Duration, actual.Duration);
                Assert.Equal(color, input.Format.Color);
            }
        }

        [Theory]
        [InlineData(1, 0, 0, 1, 3, 2, "123456")]
        [InlineData(0, 1, -1, 0, 2, 3, "415263")]
        [InlineData(-1, 0, 0, -1, 3, 2, "654321")]
        [InlineData(0, -1, 1, 0, 2, 3, "362514")]
        [InlineData(-1, 0, 0, 1, 3, 2, "321654")]
        [InlineData(1, 0, 0, -1, 3, 2, "456123")]
        [InlineData(0, 1, 1, 0, 2, 3, "142536")]
        [InlineData(0, -1, -1, 0, 2, 3, "635241")]
        public void SrgbPixelsAndStraightAlphaFollowTheExactOrientation(int a, int b, int c, int d,
            int width, int height, string order)
        {
            MediaBufferAllocator allocator = new(128);
            VideoFormat format = new(3, 2, VideoPixelFormat.Rgba32, Srgb, SampleAspectRatio.Square);
            using WritableMediaBuffer writable = allocator.Allocate(24);
            for (int index = 0; index < 6; index++)
            {
                byte value = (byte)(index + 1);
                new byte[] { value, (byte)(value + 10), (byte)(value + 20), (byte)(255 - value) }
                    .CopyTo(writable.Memory.Slice(index * 4, 4));
            }
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame input = new(format, buffer, format.CreateTightLayout());
            using VideoFrame output = SrgbPreview.Fit(input, new VideoDisplayMatrix(a, b, 0, c, d, 0, 0, 0, 1), 10, 10, allocator);
            Assert.Equal(width, output.Format.Width);
            Assert.Equal(height, output.Format.Height);
            for (int index = 0; index < order.Length; index++)
            {
                byte value = (byte)(order[index] - '0');
                Assert.Equal(new byte[] { value, (byte)(value + 10), (byte)(value + 20), (byte)(255 - value) },
                    output.Data.Slice(index * 4, 4).ToArray());
            }
        }

        [Fact]
        public void AllocatesOnlyThePreviewAndItsOwnershipSurvivesInputDisposal()
        {
            VideoFormat format = new(32, 16, VideoPixelFormat.Rgba32, Srgb, SampleAspectRatio.Square);
            MediaBufferAllocator allocator = new(32 * 16 * 4 + 4 * 2 * 4);
            using VideoFrame input = Frame(allocator, format, 32 * 16 * 4, format.CreateTightLayout());
            using VideoFrame output = SrgbPreview.Fit(input, VideoDisplayMatrix.Identity, 4, 2, allocator);
            Assert.Equal(allocator.LimitBytes, allocator.AllocatedBytes);
            byte[] pixels = output.Data.ToArray();
            input.Dispose();
            Assert.Equal(32, allocator.AllocatedBytes);
            Assert.Equal(pixels, output.Data.ToArray());
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void InvalidGeometryColorCancellationAndBudgetDoNotRetainOutputStorage()
        {
            MediaBufferAllocator allocator = new(64);
            VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32, Srgb, SampleAspectRatio.Square);
            using VideoFrame input = Frame(allocator, format, 16, format.CreateTightLayout());
            Assert.Throws<ArgumentOutOfRangeException>(() => SrgbPreview.Fit(input, VideoDisplayMatrix.Identity, 0, 1, allocator));
            Assert.Throws<NotSupportedException>(() => SrgbPreview.Fit(input, VideoDisplayMatrix.Identity with { M11 = 2 }, 1, 1, allocator));
            Assert.Throws<OperationCanceledException>(() => SrgbPreview.Fit(input, VideoDisplayMatrix.Identity, 1, 1, allocator, new CancellationToken(true)));
            MediaBufferAllocator insufficient = new(3);
            Assert.Throws<MediaMemoryLimitException>(() => SrgbPreview.Fit(input, VideoDisplayMatrix.Identity, 1, 1, insufficient));
            Assert.Equal(0, insufficient.AllocatedBytes);
            VideoFormat unknown = new(2, 2, VideoPixelFormat.Rgba32);
            using VideoFrame missing = Frame(allocator, unknown, 16, unknown.CreateTightLayout());
            Assert.Throws<NotSupportedException>(() => SrgbPreview.Fit(missing, VideoDisplayMatrix.Identity, 1, 1, allocator));
            VideoFormat hdr = new(2, 2, VideoPixelFormat.Rgba32, Srgb with { Transfer = TransferFunction.Pq }, SampleAspectRatio.Square);
            using VideoFrame unsupported = Frame(allocator, hdr, 16, hdr.CreateTightLayout());
            Assert.Throws<NotSupportedException>(() => SrgbPreview.Fit(unsupported, VideoDisplayMatrix.Identity, 1, 1, allocator));
            Assert.Equal(48, allocator.AllocatedBytes);
        }

        private static VideoFrame Frame(MediaBufferAllocator allocator, VideoFormat format, int length, PlaneLayout[] planes)
        {
            using WritableMediaBuffer writable = allocator.Allocate(length);
            for (int index = 0; index < length; index++)
            {
                writable.Memory.Span[index] = (byte)((index * 73 + 19) % 256);
            }
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes, new MediaTimestamp(7, new TimeBase(1, 30)), new MediaDuration(1, new TimeBase(1, 30)));
        }
    }
}
