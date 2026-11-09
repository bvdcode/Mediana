// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    [Collection("Allocation")]
    public class Av1InterPrecisionTests
    {
        [Fact]
        public void ConvolutionWarpAndCompoundMatchIndependentNativePrecisions()
        {
            Assert.Equal(313152, Av1InterPrecisionTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(9)]
        [InlineData(11)]
        [InlineData(13)]
        public void RejectsUnsupportedPrecision(int depth)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Av1InterPrecision<ushort>(depth));
        }

        [Theory]
        [InlineData(10)]
        [InlineData(12)]
        public void RejectsHighPrecisionByteStorage(int depth)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Av1InterPrecision<byte>(depth));
        }
    }
}
