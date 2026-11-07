// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkFixedMathTests
    {
        [Fact]
        public void NormalizedReciprocalsAndGainRatiosMatchTheIndependentDecoder() =>
            Assert.Equal(12288, OpusSilkFixedTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void ZeroNumeratorAndSaturatedPrecisionHaveExplicitResults()
        {
            Assert.Equal(0, SilkFixedMath.DividePositive(0, 1, 31));
            Assert.Equal(2147483646, SilkFixedMath.InversePositive(2, 33));
            Assert.Equal(0, SilkFixedMath.InversePositive(int.MaxValue, 1));
            Assert.Equal(2147483632, SilkFixedMath.SaturatingLeftShift(int.MaxValue, 4));
            Assert.Equal(int.MinValue, SilkFixedMath.SaturatingLeftShift(int.MinValue, 4));
        }

        [Fact]
        public void NonpositiveDenominatorsAndOutOfRangePrecisionAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkFixedMath.InversePositive(0, 47));
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkFixedMath.InversePositive(-1, 47));
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkFixedMath.InversePositive(1, 63));
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkFixedMath.DividePositive(1, 0, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkFixedMath.DividePositive(-1, 1, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkFixedMath.DividePositive(1, 1, 32));
        }
    }
}
