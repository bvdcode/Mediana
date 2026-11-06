// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class OpusLaplaceTests
    {
        [Fact]
        public void SignedEnergyResidualsAndEntropyStatesMatchIndependentReference() =>
            Assert.Equal(5257, OpusLaplaceTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Theory]
        [InlineData(0, 100)]
        [InlineData(32737, 100)]
        [InlineData(10, -1)]
        [InlineData(10, 16385)]
        public void InvalidProbabilityModelsAreRejected(int zero, int decay) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(zero, decay));

        [Fact]
        public void RepeatedDecodingUsesNoManagedAllocations()
        {
            OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int sum = 0;
            for (int at = 0; at < 10000; at++) { sum += decoder.ReadLaplace(9216, 8128); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, sum);
            Assert.Equal(0, allocated);
        }

        private static void Read(int zero, int decay)
        {
            OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
            decoder.ReadLaplace(zero, decay);
        }
    }
}
