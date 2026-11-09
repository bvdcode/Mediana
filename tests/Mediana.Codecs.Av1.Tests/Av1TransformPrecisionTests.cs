// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TransformPrecisionTests
    {
        private static string Data => Path.Combine(AppContext.BaseDirectory, "Data");

        [Theory]
        [InlineData(16)]
        [InlineData(18)]
        [InlineData(20)]
        public void EveryTransformLengthUsesTheNormativeSignedRange(int precision) =>
            Av1TransformPrecisionTestInput.VerifyOneDimension(Data, precision);

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void RectangularTransformsKeepIndependentRowAndColumnPrecision(int bitDepth) =>
            Av1TransformPrecisionTestInput.VerifyTwoDimensions(Data, bitDepth);

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void LosslessResidualsRetainTheDeclaredSampleRange(int bitDepth) =>
            Av1TransformPrecisionTestInput.VerifyLossless(Data, bitDepth);

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        [InlineData(16)]
        public void InvalidDepthFailsBeforeChangingTheCoefficients(int bitDepth)
        {
            int[] values = Enumerable.Range(0, 16).ToArray();
            int[] expected = values.ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1InverseTransform.Inverse(values, 4, 4, 0, bitDepth));
            Assert.Equal(expected, values);
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1LosslessTransform.Inverse(values, bitDepth));
            Assert.Equal(expected, values);
        }

        [Theory]
        [InlineData(15)]
        [InlineData(17)]
        [InlineData(32)]
        public void InvalidOneDimensionalRangeFailsBeforeChangingTheCoefficients(int precision)
        {
            int[] values = [1, 2, 3, 4];
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1InverseTransform.Transform(values, 0, precision));
            Assert.Equal(new[] { 1, 2, 3, 4 }, values);
        }
    }
}
