// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;

namespace Mediana.Tests
{
    public class AacPrerollTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        public void PrerollSuppressesOnlyRequestedOutputAndResetKeepsRetainedStorage(int count)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using (MediaPacket packet = Packet(allocator))
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Decoder(allocator))
            {
                IPacketPrerollDecoder preroll = Assert.IsAssignableFrom<IPacketPrerollDecoder>(decoder);
                Assert.Equal(1, preroll.RequiredPrerollPackets);
                byte[] expected;
                for (int index = 0; index < count; index++)
                {
                    decoder.TrySend(packet);
                    using AudioFrame frame = decoder.Receive().Value;
                }
                decoder.TrySend(packet);
                using AudioFrame reference = decoder.Receive().Value;
                expected = reference.GetPlane(0).ToArray();
                preroll.ResetForPreroll(count, true);
                for (int index = 0; index < count; index++)
                {
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                    Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                }
                decoder.TrySend(packet);
                Assert.Throws<ArgumentOutOfRangeException>(() => preroll.ResetForPreroll(-1, false));
                using AudioFrame output = decoder.Receive().Value;
                Assert.Equal(expected, output.GetPlane(0).ToArray());
                decoder.Dispose();
                Assert.Equal(expected, reference.GetPlane(0).ToArray());
                Assert.Equal(expected, output.GetPlane(0).ToArray());
                Assert.Equal(packet.Data.Length + 2 * expected.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void IncompletePrerollFaultsAndResetRestoresOrdinaryStartup()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using MediaPacket packet = Packet(allocator);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = Decoder(allocator);
            IPacketPrerollDecoder preroll = (IPacketPrerollDecoder)decoder;
            preroll.ResetForPreroll(long.MaxValue, false);
            decoder.TrySend(packet);
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            Assert.Throws<InvalidDataException>(() => decoder.CompleteInput());
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            decoder.TrySend(packet);
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(1024, frame.SamplesPerChannel);
        }

        private static IMediaCodec<MediaPacket, AudioFrame> Decoder(MediaBufferAllocator allocator) =>
            new AacDecoderFactory().Create(new EncodedStreamInfo(0, MediaKind.Audio, "aac", StreamFormats.Aac,
                new TimeBase(1, 48000), new byte[] { 0x11, 0x88 }), allocator);

        private static MediaPacket Packet(MediaBufferAllocator allocator)
        {
            byte[] data = AacTestPackets.Mono();
            using WritableMediaBuffer writable = allocator.Allocate(data.Length);
            data.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new MediaPacket(0, buffer, new MediaTimestamp(0, new(1, 48000)));
        }
    }
}
