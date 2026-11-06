// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkResamplerTests
    {
        [Fact]
        public void ConsecutiveBandwidthConversionsMatchIndependentPcm() =>
            Assert.Equal(768, OpusSilkResamplerTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Theory]
        [InlineData(8000)]
        [InlineData(12000)]
        [InlineData(16000)]
        public void ConsecutiveFramesAllocateNothingAndResetMatchesFreshState(int sampleRate)
        {
            MediaBufferAllocator allocator = new(2048);
            using SilkResampler resampler = new(sampleRate, allocator);
            using SilkResampler fresh = new(sampleRate, allocator);
            Span<short> input = stackalloc short[sampleRate / 50], output = stackalloc short[960], expected = stackalloc short[960];
            for (int at = 0; at < input.Length; at++) { input[at] = (short)(at * 93 - 10000); }
            resampler.Process(input, output);
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++) { resampler.Process(input, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            resampler.Reset(); resampler.Process(input, output); fresh.Process(input, expected);
            Assert.True(output.SequenceEqual(expected));
            resampler.Dispose(); fresh.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
