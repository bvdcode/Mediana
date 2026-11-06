// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltSynthesisTests
    {
        [Fact]
        public void ChannelMappingOverlapPitchFilteringAndPcmMatchIndependentSynthesis() =>
            Assert.Equal(128, OpusSynthesisTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RepeatedSynthesisReusesCallerStorageWithoutAllocating()
        {
            MediaBufferAllocator allocator = new(32 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            const int stride = CeltPitchPostfilter.HistorySamples + 960 + 60;
            Span<float> signal = stackalloc float[2 * stride], frequency = stackalloc float[1920];
            Span<float> workspace = stackalloc float[960], output = stackalloc float[1920], memory = stackalloc float[2];
            signal.Clear(); frequency.Fill(.125f); memory.Clear();
            CeltBandMode mode = new(2, 3, 0, 21);
            CeltPostFilterParameters filter = new(23, .375f, 2);
            for (int at = 0; at < 2; at++) { Synthesize(plan, mode, filter, signal, frequency, workspace, output, memory, stride); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++) { Synthesize(plan, mode, filter, signal, frequency, workspace, output, memory, stride); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            plan.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void InvalidFilterParametersDoNotChangeTheSignal()
        {
            float[] signal = Enumerable.Repeat(17f, 1400).ToArray(), window = new float[120];
            Assert.Throws<ArgumentOutOfRangeException>(() => CeltPitchPostfilter.Apply(signal, 1026, 120, default, new(15, float.NaN, 0), window));
            Assert.Throws<ArgumentOutOfRangeException>(() => CeltPitchPostfilter.Apply(signal, 1026, 120, default, new(15, .25f, 3), window));
            Assert.Throws<ArgumentOutOfRangeException>(() => CeltPitchPostfilter.Apply(signal, 1026, 120, default, new(1025, .25f, 0), window));
            Assert.All(signal, value => Assert.Equal(17, value));
        }

        [Fact]
        public void ZeroGainPreservesTheWholeBorrowedSignal()
        {
            float[] signal = Enumerable.Range(0, 1400).Select(value => (float)value).ToArray();
            CeltPitchPostfilter.Apply(signal, 1026, 240, default, default, new float[120]);
            for (int at = 0; at < signal.Length; at++) { Assert.Equal(at, signal[at]); }
        }

        private static void Synthesize(CeltTransformPlan plan, CeltBandMode mode, CeltPostFilterParameters filter,
            Span<float> signal, Span<float> frequency, Span<float> workspace, Span<float> output, Span<float> memory, int stride)
        {
            CeltSynthesis.Apply(plan, mode, true, 2, frequency, signal[CeltPitchPostfilter.HistorySamples..], stride, workspace);
            for (int channel = 0; channel < 2; channel++)
            {
                Span<float> values = signal.Slice(channel * stride, stride);
                CeltPitchPostfilter.Apply(values, CeltPitchPostfilter.HistorySamples, 960, filter, filter, plan.Window);
            }
            CeltDeemphasis.Apply(signal[CeltPitchPostfilter.HistorySamples..], 960, 2, stride, output, memory);
            for (int channel = 0; channel < 2; channel++)
            {
                Span<float> values = signal.Slice(channel * stride, stride);
                values.Slice(960, CeltPitchPostfilter.HistorySamples + 60).CopyTo(values);
            }
        }
    }
}
