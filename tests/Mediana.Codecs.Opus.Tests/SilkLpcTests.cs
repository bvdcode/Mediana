// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkLpcTests
    {
        [Fact]
        public void PolynomialConversionFittingAndInversePredictionGainMatchTheIndependentDecoder() =>
            Assert.Equal(6144, OpusSilkLpcTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Theory]
        [InlineData(10)]
        [InlineData(16)]
        public void RepeatedLpcConversionAllocatesNoManagedStorage(int order)
        {
            Span<short> frequencies = stackalloc short[16], coefficients = stackalloc short[16];
            for (int at = 0; at < order; at++) { frequencies[at] = (short)((at + 1) * 32768 / (order + 1)); }
            for (int at = 0; at < 2; at++) { SilkLpcConverter.Convert(frequencies[..order], coefficients); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++) { SilkLpcConverter.Convert(frequencies[..order], coefficients); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void NegativeFrequencyPreservesTheDestination()
        {
            short[] frequencies = new short[16], coefficients = Enumerable.Repeat((short)17, 16).ToArray();
            frequencies[15] = -1;
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkLpcConverter.Convert(frequencies, coefficients));
            Assert.All(coefficients, value => Assert.Equal(17, value));
        }
    }
}
