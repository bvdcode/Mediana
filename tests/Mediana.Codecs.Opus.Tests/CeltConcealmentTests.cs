// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltConcealmentTests
    {
        [Fact]
        public void PeriodicAndNoiseRecoveryUseOnlyReservedStorage()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using CeltFrameDecoder decoder = new(plan, codebook, allocator, 2, false);
            CeltBandMode mode = new(2, 3, 0, 21);
            Span<float> output = stackalloc float[1920];
            decoder.Decode([255, 254], mode, output); decoder.Decode([255, 254], mode, output);
            decoder.Conceal(mode, output);
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++) { decoder.Conceal(mode, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            decoder.Dispose(); codebook.Dispose(); plan.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void LostPacketsAndResumedSpeechMatchIndependentPcm() =>
            Assert.Equal(6144, OpusCeltRecoveryTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void ConcealmentFromRecordedHistoriesMatchesEveryOutputAndFoldedOverlap()
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "celt-concealment.bin")));
            Assert.True(reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSPLP1"u8));
            int count = reader.ReadInt32();
            MediaBufferAllocator allocator = new(32 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltRecoveryState state = new(allocator);
            Span<float> signal = stackalloc float[3128];
            for (int test = 0; test < count; test++)
            {
                int samples = reader.ReadInt32(), period = reader.ReadInt32(); float gain = reader.ReadSingle(); int tapset = reader.ReadInt32();
                state.Pitch = reader.ReadInt32(); state.PreviousPeriodic = reader.ReadInt32() != 0;
                for (int at = 0; at < 2168; at++) { signal[at] = reader.ReadSingle(); }
                for (int at = 0; at < 24; at++) { state.Coefficients[at] = reader.ReadSingle(); }
                CeltConcealment.Periodic(state, signal, 1, 3128, samples, plan.Window);
                signal.Slice(samples, 2168).CopyTo(signal);
                for (int at = 0; at < 2168; at++)
                {
                    float expected = reader.ReadSingle();
                    Assert.True(Math.Abs(signal[at] - expected) < 1e-4f * Math.Max(1, Math.Abs(expected)), $"History {test}/{at}: {signal[at]} / {expected}");
                }
                for (int at = 0; at < 24; at++)
                {
                    float expected = reader.ReadSingle();
                    Assert.True(Math.Abs(state.Coefficients[at] - expected) < 1e-5f, $"LPC {test}/{at}: {state.Coefficients[at]} / {expected}");
                }
                Assert.Equal(reader.ReadInt32(), state.Pitch);
                CeltConcealment.Fold(signal, 1, 3128, new(period, gain, tapset), plan.Window);
                for (int at = 0; at < 60; at++)
                {
                    float expected = reader.ReadSingle();
                    Assert.True(Math.Abs(signal[2048 + at] - expected) < 1e-4f * Math.Max(1, Math.Abs(expected)), $"Fold {test}/{at}: {signal[2048 + at]} / {expected}");
                }
            }
        }
    }
}
