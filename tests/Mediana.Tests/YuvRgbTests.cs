using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class YuvRgbTests
    {
        [Theory]
        [InlineData(MatrixCoefficients.Bt601, 0.8505, 0.32146593, 0.5)]
        [InlineData(MatrixCoefficients.Bt709, 0.8937, 0.38296893, 0.5)]
        [InlineData(MatrixCoefficients.Fcc, 0.85, 0.32203390, 0.5)]
        [InlineData(MatrixCoefficients.Smpte240M, 0.894, 0.38084451, 0.5)]
        [InlineData(MatrixCoefficients.Bt2020NonConstant, 0.86865, 0.35716172, 0.5)]
        public void FullRangeMatricesPreserveNeutralAndSeparateColorDifferences(MatrixCoefficients coefficients,
            double expectedRed, double expectedGreen, double expectedBlue)
        {
            YuvRgbMatrix matrix = YuvRgbMatrix.Create(coefficients, ColorRange.Full);
            Assert.Equal((0.0, 0.0, 0.0), matrix.Convert(0, 128, 128));
            Assert.Equal((1.0, 1.0, 1.0), matrix.Convert(255, 128, 128));
            (double red, double green, double blue) = matrix.Convert(127.5, 128, 191.75);
            Assert.Equal(expectedRed, red, 7);
            Assert.Equal(expectedGreen, green, 7);
            Assert.Equal(expectedBlue, blue, 7);
        }

        [Fact]
        public void LimitedRangeUses219LumaAnd224ChromaStepsAndPreservesExcursions()
        {
            YuvRgbMatrix matrix = YuvRgbMatrix.Create(MatrixCoefficients.Bt601, ColorRange.Limited);
            Assert.Equal((0.0, 0.0, 0.0), matrix.Convert(16, 128, 128));
            Assert.Equal((1.0, 1.0, 1.0), matrix.Convert(235, 128, 128));
            (double red, double green, double blue) = matrix.Convert(81, 90, 240);
            Assert.InRange(red, 0.997, 0.999);
            Assert.InRange(green, -0.002, 0);
            Assert.InRange(blue, -0.004, -0.003);
            Assert.True(matrix.Convert(0, 128, 128).Red < 0);
            Assert.True(matrix.Convert(255, 128, 128).Red > 1);
            Assert.Throws<NotSupportedException>(() => YuvRgbMatrix.Create(MatrixCoefficients.Unspecified, ColorRange.Full));
            Assert.Throws<NotSupportedException>(() => YuvRgbMatrix.Create(MatrixCoefficients.Bt709, ColorRange.Unspecified));
            Assert.Throws<ArgumentOutOfRangeException>(() => YuvRgbMatrix.Create((MatrixCoefficients)int.MaxValue, ColorRange.Full));
        }

        [Theory]
        [InlineData(ChromaLocation.Left, 92.5)]
        [InlineData(ChromaLocation.Center, 71.25)]
        [InlineData(ChromaLocation.TopLeft, 135)]
        [InlineData(ChromaLocation.Top, 117.5)]
        [InlineData(ChromaLocation.BottomLeft, 50)]
        [InlineData(ChromaLocation.Bottom, 25)]
        public void ChromaInterpolationHonorsAllSixLocationsAndPlaneStride(ChromaLocation location, double expected)
        {
            MediaBufferAllocator allocator = new(64);
            using WritableMediaBuffer writable = allocator.Allocate(64);
            byte[] chromaTop = [0, 100];
            byte[] chromaBottom = [200, 240];
            chromaTop.CopyTo(writable.Memory.Span[24..]);
            chromaBottom.CopyTo(writable.Memory.Span[29..]);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame frame = new(new VideoFormat(4, 4, VideoPixelFormat.Yuv420P8), buffer,
                [new PlaneLayout(0, 6, 4, 4), new PlaneLayout(24, 5, 2, 2), new PlaneLayout(40, 4, 2, 2)]);
            BilinearChromaRow middle = new(frame, 1, 1, location);
            Assert.Equal(expected, middle.Sample(1));
            BilinearChromaRow first = new(frame, 1, 0, location);
            Assert.Equal(0, first.Sample(0));
            BilinearChromaRow last = new(frame, 1, 3, location);
            Assert.Equal(240, last.Sample(3));
        }
    }
}
