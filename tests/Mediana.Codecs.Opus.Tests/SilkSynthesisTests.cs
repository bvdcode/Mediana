// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkSynthesisTests
    {
        [Fact]
        public void FixedPointPitchWhiteningPredictionAndPcmMatchIndependentSynthesis() =>
            Assert.Equal(768, OpusSilkSynthesisTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Theory]
        [InlineData(8000)]
        [InlineData(12000)]
        [InlineData(16000)]
        public void RepeatedVoicedSynthesisReusesBudgetedState(int sampleRate)
        {
            MediaBufferAllocator allocator = new(16 * 1024);
            using SilkSynthesisState state = new(sampleRate, allocator);
            SilkFrameMode mode = new(sampleRate, 4);
            Span<int> gains = stackalloc int[4], lags = stackalloc int[4];
            Span<short> lpc = stackalloc short[32], ltp = stackalloc short[20], pulses = stackalloc short[320], output = stackalloc short[320];
            gains.Fill(81920); lags.Fill(mode.RateKhz * 5); lpc.Clear(); ltp.Clear(); pulses.Fill(1);
            for (int at = 0; at < 4; at++) { ltp[at * 5 + 2] = 4096; }
            SilkFrameIndices indices = new(SilkSignalType.Voiced, 0, 2, 1, default);
            SilkParameterBuffers parameters = new(gains, lpc, lags, ltp);
            for (int at = 0; at < 2; at++) { SilkSynthesis.Process(state, mode, indices, new(2, 15565), parameters, pulses, output); }
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++) { SilkSynthesis.Process(state, mode, indices, new(2, 15565), parameters, pulses, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            state.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
