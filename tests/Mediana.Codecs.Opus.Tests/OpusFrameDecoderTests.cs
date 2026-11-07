// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class OpusFrameDecoderTests
    {
        [Fact]
        public void HybridLayersShareEntropyAndMatchIndependentlyEncodedPcm() =>
            Assert.Equal(576, OpusCeltPcmTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), "decoded-hybrid.bin"));

        [Fact]
        public void SequentialModeAndBandwidthTransitionsMatchIndependentPcm() =>
            Assert.Equal(768, OpusCeltPcmTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), "decoded-transitions.bin"));
        [Fact]
        public void RepeatedHybridFramesAllocateNothingAndResetReproducesFreshOutput()
        {
            byte[] packet = ReadHybridPacket();
            Span<Range> frames = stackalloc Range[48];
            OpusPacketInfo.ReadFrames(packet, frames);
            OpusPacketConfiguration configuration = OpusPacketConfiguration.FromToc(packet[0]);
            ReadOnlySpan<byte> frame = packet.AsSpan()[frames[0]];
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            long tables = allocator.AllocatedBytes;
            using OpusFrameDecoder decoder = new(plan, codebook, allocator, 1, true);
            Span<float> output = stackalloc float[960], initial = stackalloc float[960];
            int samples = decoder.Decode(frame, configuration, initial);
            uint initialRange = decoder.FinalRange;
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++) { decoder.Decode(frame, configuration, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            decoder.Reset(); Assert.Equal(samples, decoder.Decode(frame, configuration, output));
            Assert.True(output[..samples].SequenceEqual(initial[..samples]));
            Assert.Equal(initialRange, decoder.FinalRange);
            decoder.Dispose(); Assert.Equal(tables, allocator.AllocatedBytes);
        }

        [Fact]
        public void CancellationBeforeHybridDecodingPreservesOutputAndState()
        {
            byte[] packet = ReadHybridPacket();
            Span<Range> frames = stackalloc Range[48];
            OpusPacketInfo.ReadFrames(packet, frames);
            byte[] frame = packet.AsSpan()[frames[0]].ToArray();
            OpusPacketConfiguration configuration = OpusPacketConfiguration.FromToc(packet[0]);
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using OpusFrameDecoder decoder = new(plan, codebook, allocator, 1, true);
            float[] output = new float[960]; Array.Fill(output, float.NaN);
            Assert.Throws<OperationCanceledException>(() => decoder.Decode(frame, configuration, output, new(true)));
            Assert.All(output, value => Assert.True(float.IsNaN(value)));
            Assert.Equal(0u, decoder.FinalRange);
            Assert.Equal(configuration.SamplesPerFrame, decoder.Decode(frame, configuration, output));
        }

        private static byte[] ReadHybridPacket()
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "decoded-hybrid.bin")));
            reader.ReadBytes(24);
            return reader.ReadBytes(reader.ReadInt32());
        }
    }
}
