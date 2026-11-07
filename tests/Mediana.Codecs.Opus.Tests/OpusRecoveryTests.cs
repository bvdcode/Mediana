// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using System.IO.Compression;

namespace Mediana.Codecs.Opus.Tests
{
    public class OpusRecoveryTests
    {
        [Fact]
        public void ModeChangesDtxAndLossRecoveryDoNotAllocateManagedWorkingStorage()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "decoded-silk.bin.gz");
            using GZipStream stream = new(File.OpenRead(path), CompressionMode.Decompress);
            using BinaryReader reader = new(stream); reader.ReadBytes(24);
            byte[] packet = reader.ReadBytes(reader.ReadInt32());
            Span<Range> ranges = stackalloc Range[48]; OpusPacketInfo.ReadFrames(packet, ranges);
            ReadOnlySpan<byte> silkPayload = packet.AsSpan()[ranges[0]];
            OpusPacketConfiguration silkMode = OpusPacketConfiguration.FromToc(packet[0]), celtMode = OpusPacketConfiguration.FromToc(248);
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using OpusFrameDecoder decoder = new(plan, codebook, allocator, 2, false);
            Span<float> output = stackalloc float[1920];
            decoder.Decode(silkPayload, silkMode, output); decoder.Conceal(480, output);
            decoder.Decode([255, 254], celtMode, output); decoder.Conceal(960, output);
            long before = GC.GetAllocatedBytesForCurrentThread(), budget = allocator.AllocatedBytes;
            for (int at = 0; at < 200; at++)
            {
                decoder.Decode(silkPayload, silkMode, output); decoder.Decode([], silkMode, output);
                decoder.Decode([255, 254], celtMode, output); decoder.Decode([], celtMode, output);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(budget, allocator.AllocatedBytes);
            decoder.Dispose(); codebook.Dispose(); plan.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void HybridSilkLayerRecoversWithExactFixedPointPcm() =>
            Assert.Equal(1536, OpusSilkRecoveryTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), "decoded-hybrid-silk-recovery.bin.gz"));

        [Theory]
        [InlineData("decoded-silk-recovery.bin.gz", 4608)]
        [InlineData("decoded-celt-recovery.bin.gz", 6144)]
        [InlineData("decoded-hybrid-recovery.bin.gz", 1536)]
        [InlineData("decoded-unprimed-transitions.bin.gz", 1024)]
        public void CombinedDecoderRetainsStateAcrossLossDtxAndResumedPackets(string filename, int count) =>
            Assert.Equal(count, OpusRecoveryTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), filename));
    }
}
