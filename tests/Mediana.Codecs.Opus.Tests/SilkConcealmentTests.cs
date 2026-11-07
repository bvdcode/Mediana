// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkConcealmentTests
    {
        [Fact]
        public void NormalPacketsLossBurstsAndDtxMatchIndependentPcm() =>
            Assert.Equal(4608, OpusSilkRecoveryTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void SequentialLostFramesMatchIndependentPcmAndRetainedPredictionState() =>
            Assert.Equal(288, OpusSilkConcealmentTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));
        [Fact]
        public void RepeatedLostPacketsReuseTheReservedChannelAndResamplingStorage()
        {
            MediaBufferAllocator allocator = new(32 * 1024);
            using SilkFrameDecoder decoder = new(16000, 2, allocator);
            Span<float> output = stackalloc float[1920];
            decoder.Conceal(960, output);
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++) { decoder.Conceal(960, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            decoder.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RepeatedLossReconstructionUsesOnlyTheReservedWorkingStorage()
        {
            MediaBufferAllocator allocator = new(16 * 1024);
            using SilkSynthesisState synthesis = new(16000, allocator);
            using SilkRecoveryState recovery = new(16000, allocator);
            SilkFrameMode mode = new(16000, 4);
            Span<short> output = stackalloc short[320];
            SilkConcealment.Synthesize(recovery, synthesis, mode, true, output);
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++) { SilkConcealment.Synthesize(recovery, synthesis, mode, false, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            synthesis.Dispose(); recovery.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
