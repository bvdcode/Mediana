using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264EightByEightTests
    {
        [Fact]
        public async Task AllDirectionalModesMatchEveryIndependentPredictedSample() =>
            await H264EightByEightTestInput.VerifyPredictionVectorsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));

        [Theory]
        [InlineData(0, 5)]
        [InlineData(1, 6)]
        [InlineData(2, 7)]
        [InlineData(3, 7)]
        [InlineData(4, 8)]
        [InlineData(5, 9)]
        [InlineData(6, 10)]
        [InlineData(35, 288)]
        [InlineData(36, 320)]
        [InlineData(51, 1792)]
        public void ConstantFrequencyMatchesNormativeScaleAndRounding(int qp, int expected)
        {
            int[] coefficients = new int[64];
            coefficients[0] = 64;
            int[] residual = new int[64];
            H264InverseTransform8x8.Residual(coefficients, qp, residual);
            Assert.All(residual, value => Assert.Equal(expected, value));
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(-1, false)]
        [InlineData(1, true)]
        [InlineData(-1, true)]
        public void OddFrequencyPreservesOrientationAndSignedRounding(int sign, bool vertical)
        {
            int[] coefficients = new int[64];
            coefficients[vertical ? 2 : 1] = sign * 64;
            int[] residual = new int[64];
            H264InverseTransform8x8.Residual(coefficients, 0, residual);
            int[] line = [7, 6, 4, 2, -2, -4, -6, -7];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Assert.Equal(sign * line[vertical ? y : x], residual[y * 8 + x]);
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void DirectionalPredictionRejectsMissingNeighbours(int mode) =>
            Assert.Throws<InvalidDataException>(() => H264IntraPrediction.Luma8x8(mode, [], [], null, new byte[64]));

        [Fact]
        public void FilteredDcHandlesAbsentSidesAndCorner()
        {
            byte[] output = new byte[64];
            H264IntraPrediction.Luma8x8(2, [], [], null, output);
            Assert.All(output, value => Assert.Equal(128, value));
            H264IntraPrediction.Luma8x8(2, [], [0, 0, 0, 0, 0, 0, 0, 255], null, output);
            Assert.All(output, value => Assert.Equal(32, value));
            H264IntraPrediction.Luma8x8(2, [0, 0, 0, 0, 0, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0], [], null, output);
            Assert.All(output, value => Assert.Equal(24, value));
        }
    }
}
