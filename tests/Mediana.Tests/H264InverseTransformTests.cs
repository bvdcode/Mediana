using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264InverseTransformTests
    {
        [Theory]
        [InlineData(0, 10)]
        [InlineData(6, 20)]
        [InlineData(12, 40)]
        [InlineData(24, 160)]
        public void DcCoefficientProducesConstantResidualAndDoublesEverySixQp(int qp, int expected)
        {
            int[] coefficients = new int[16];
            coefficients[0] = 64;
            int[] residual = new int[16];
            H264InverseTransform.Residual4x4(coefficients, qp, residual);
            Assert.All(residual, value => Assert.Equal(expected, value));
        }

        [Fact]
        public void HorizontalFrequencyKeepsSignedRounding()
        {
            int[] coefficients = new int[16];
            coefficients[1] = 64;
            int[] residual = new int[16];
            H264InverseTransform.Residual4x4(coefficients, 0, residual);
            Assert.Equal(new[] { 13, 7, -6, -13, 13, 7, -6, -13, 13, 7, -6, -13, 13, 7, -6, -13 }, residual);
        }

        [Fact]
        public void AlreadyScaledDcIsNotScaledAgain()
        {
            int[] residual = new int[16];
            H264InverseTransform.Residual4x4(new int[15], 51, residual, scaledDc: 640);
            Assert.All(residual, value => Assert.Equal(10, value));
        }

        [Theory]
        [InlineData(0, 160, 320)]
        [InlineData(6, 320, 640)]
        [InlineData(12, 640, 1280)]
        public void DcHadamardTransformsDistributeTheConstantCoefficient(int qp, int luma, int chroma)
        {
            int[] coefficients = new int[16];
            coefficients[0] = 64;
            int[] dc = new int[16];
            H264InverseTransform.LumaDc(coefficients, qp, dc);
            Assert.All(dc, value => Assert.Equal(luma, value));
            int[] chromaDc = new int[4];
            H264InverseTransform.ChromaDc([64, 0, 0, 0], qp, chromaDc);
            Assert.All(chromaDc, value => Assert.Equal(chroma, value));
        }

        [Fact]
        public void LumaHadamardMaintainsRasterOrderAfterInverseScan()
        {
            int[] coefficients = new int[16];
            coefficients[1] = 4;
            int[] dc = new int[16];
            H264InverseTransform.LumaDc(coefficients, 0, dc);
            Assert.Equal(new[] { 10, 10, -10, -10, 10, 10, -10, -10, 10, 10, -10, -10, 10, 10, -10, -10 }, dc);
        }

        [Theory]
        [InlineData(0, -12, 0)]
        [InlineData(29, 0, 29)]
        [InlineData(30, 0, 29)]
        [InlineData(35, 0, 33)]
        [InlineData(51, 12, 39)]
        public void MapsChromaQuantizerThroughItsNonlinearTable(int lumaQp, int offset, int expected) =>
            Assert.Equal(expected, H264InverseTransform.ChromaQp(lumaQp, offset));
    }
}
