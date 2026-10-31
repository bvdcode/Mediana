// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    public class H264PcmEncoderTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(0, 6)]
        [InlineData(1, 6)]
        public async Task MixedPcmSequencesMatchIndependentPixels(int mode, int quantizer) =>
            await H264PcmEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"), mode, quantizer);

        [Fact]
        public void FixedQuantizerNoiseRespectsTheMacroblockLayerBitLimit()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using WritableMediaBuffer storage = allocator.Allocate(384);
            new Random(172).NextBytes(storage.Memory.Span);
            using MediaBuffer buffer = storage.Publish();
            using VideoFrame frame = new(format, buffer, format.CreateTightLayout(), new(0, new(1, 24)), new(1, new(1, 24)));
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 24), allocator, new() { Quantizer = 0 });
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(frame));
            using MediaPacket packet = encoder.Receive().Value!;
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
            H264BitReader bits = H264Nal.Payload(packet.Data.Span[4..], 5);
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, packet.Data.Span[4]);
            Assert.Equal(0, header.Qp);
            int start = bits.BitPosition;
            Assert.Equal(25, bits.ReadUe(25));
            bits.AlignZero();
            foreach (byte sample in frame.Data.Span)
            {
                Assert.Equal(sample, bits.ReadBits(8));
            }
            int macroblockBits = bits.BitPosition - start;
            Assert.True(macroblockBits <= 3200, $"The macroblock contains {macroblockBits} bits.");
            bits.ReadTrailingBits();
            using H264Encoder exact = H264Encoder.Create(format, new(1, 24), allocator,
                new() { Quantizer = 0, MaximumPacketBytes = packet.Data.Length });
            Assert.Equal(CodecInputStatus.Accepted, exact.TrySend(frame));
            using MediaPacket bounded = exact.Receive().Value!;
            Assert.Equal(packet.Data.ToArray(), bounded.Data.ToArray());
            exact.Dispose();
            using H264Encoder small = H264Encoder.Create(format, new(1, 24), allocator,
                new() { Quantizer = 0, MaximumPacketBytes = packet.Data.Length - 1 });
            long held = allocator.AllocatedBytes;
            NotSupportedException failure = Assert.Throws<NotSupportedException>(() => small.TrySend(frame));
            Assert.Contains("fixed quantizer", failure.Message);
            Assert.Equal(held, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => small.Receive());
            small.Reset();
            Assert.Throws<NotSupportedException>(() => small.TrySend(frame));
            Assert.Equal(held, allocator.AllocatedBytes);
        }
    }
}
