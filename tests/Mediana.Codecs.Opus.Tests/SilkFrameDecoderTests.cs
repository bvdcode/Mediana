// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkFrameDecoderTests
    {
        [Fact]
        public void IndependentlyEncodedSequentialSilkPacketsMatchEveryPcmSampleAndFinalRange() =>
            Assert.Equal(3072, OpusSilkPcmTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RepeatedFramesAllocateNothingAndResetReproducesFreshPcm()
        {
            byte[] packet = ReadFirstPacket();
            Span<Range> frames = stackalloc Range[48];
            OpusPacketInfo.ReadFrames(packet, frames);
            OpusPacketConfiguration configuration = OpusPacketConfiguration.FromToc(packet[0]);
            ReadOnlySpan<byte> payload = packet.AsSpan()[frames[0]];
            MediaBufferAllocator allocator = new(64 * 1024);
            using SilkFrameDecoder decoder = new(8000, 1, allocator);
            using SilkFrameDecoder fresh = new(8000, 1, allocator);
            Span<float> output = stackalloc float[480], expected = stackalloc float[480];
            decoder.Decode(payload, configuration, output);
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++) { decoder.Decode(payload, configuration, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            decoder.Reset(); decoder.Decode(payload, configuration, output); fresh.Decode(payload, configuration, expected);
            Assert.True(output.SequenceEqual(expected)); Assert.Equal(fresh.FinalRange, decoder.FinalRange);
            decoder.Dispose(); fresh.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3500)]
        [InlineData(6800)]
        [InlineData(7200)]
        [InlineData(8000)]
        public void ConstructionReleasesEveryPreviousLeaseWhenTheBudgetRunsOut(long limit)
        {
            MediaBufferAllocator allocator = new(limit);
            Assert.Throws<MediaMemoryLimitException>(() => new SilkFrameDecoder(8000, 1, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void PrecancelledFramesPreserveTheOutputAndDecoderState()
        {
            byte[] packet = ReadFirstPacket();
            Span<Range> frames = stackalloc Range[48];
            OpusPacketInfo.ReadFrames(packet, frames);
            OpusPacketConfiguration configuration = OpusPacketConfiguration.FromToc(packet[0]);
            byte[] payload = packet.AsSpan()[frames[0]].ToArray();
            MediaBufferAllocator allocator = new(32 * 1024);
            using SilkFrameDecoder decoder = new(8000, 1, allocator);
            float[] output = new float[480]; Array.Fill(output, float.NaN);
            Assert.Throws<OperationCanceledException>(() => decoder.Decode(payload, configuration, output, new(true)));
            Assert.All(output, value => Assert.True(float.IsNaN(value)));
            Assert.Equal(0u, decoder.FinalRange);
            Assert.Equal(480, decoder.Decode(payload, configuration, output));
            Assert.Equal(1296506624u, decoder.FinalRange);
        }

        [Fact]
        public void BandwidthChangesReuseTheReservedWorkspace()
        {
            MediaBufferAllocator allocator = new(32 * 1024);
            using SilkFrameDecoder decoder = new(8000, 2, allocator);
            decoder.SetSampleRate(16000); decoder.SetSampleRate(12000); decoder.SetSampleRate(8000);
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++)
            {
                decoder.SetSampleRate(16000); decoder.SetSampleRate(12000); decoder.SetSampleRate(8000);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            Assert.Equal(8000, decoder.SampleRate);
            decoder.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] ReadFirstPacket()
        {
            using GZipStream stream = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "decoded-silk.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(stream);
            reader.ReadBytes(24);
            return reader.ReadBytes(reader.ReadInt32());
        }
    }
}
