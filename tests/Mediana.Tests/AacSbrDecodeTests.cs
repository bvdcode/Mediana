// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacSbrDecodeTests
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        public async Task CompleteSequenceMatchesIndependentPcmWithBothSignalingModes(int channels, bool explicitSbr)
        {
            await AacSbrDecodeTestInput.VerifySequenceAsync(Root, channels, explicitSbr);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        public async Task SingleRateSynthesisMatchesCompleteIndependentPcmWithForwardAndSyncSignaling(int channels, bool explicitSbr)
        {
            await AacSbrDecodeTestInput.VerifySequenceAsync(Root, channels, explicitSbr, singleRate: true);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task ResetRestoresAllSynthesisStateAndRetainedOutputRemainsIndependent(int channels)
        {
            byte[][] packets = await ReadPacketsAsync(channels);
            MediaBufferAllocator allocator = new(384 * 1024);
            AudioFrame? retained = null;
            try
            {
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(channels, true), allocator);
                using MediaPacket first = Packet(allocator, packets[0], 0);
                decoder.TrySend(first);
                retained = decoder.Receive().Value;
                byte[] expected = retained.GetPlane(0).ToArray();
                for (int index = 1; index < 5; index++)
                {
                    using MediaPacket packet = Packet(allocator, packets[index], index);
                    decoder.TrySend(packet);
                    using AudioFrame frame = decoder.Receive().Value;
                }
                decoder.Reset();
                decoder.TrySend(first);
                using AudioFrame repeated = decoder.Receive().Value;
                Assert.Equal(expected, repeated.GetPlane(0).ToArray());
                decoder.Dispose();
                Assert.Equal(expected, retained.GetPlane(0).ToArray());
                Assert.Equal(first.Data.Length + 2 * expected.Length, allocator.AllocatedBytes);
            }
            finally
            {
                retained?.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task TruncationCancellationAndMissingInitialHeaderReleaseAllWorkspace()
        {
            byte[][] packets = await ReadPacketsAsync(2);
            MediaBufferAllocator allocator = new(256 * 1024);
            using MediaPacket first = Packet(allocator, packets[0], 0);
            using MediaPacket truncated = Packet(allocator, packets[0][..^1], 0);
            using MediaPacket following = Packet(allocator, packets[1], 1);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(2, true), allocator);
            long inputBytes = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(truncated));
            Assert.Equal(inputBytes, allocator.AllocatedBytes);
            decoder.Reset();
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(following));
            Assert.Equal(inputBytes, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(first);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            Assert.Throws<OperationCanceledException>(() => decoder.Receive(cancellation.Token));
            Assert.Equal(inputBytes, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(first);
            using AudioFrame output = decoder.Receive().Value;
            Assert.Equal(2048, output.SamplesPerChannel);
        }

        [Fact]
        public async Task SbrRejectsInsufficientNonOriginPrerollAndPreservesOriginalStreamStartup()
        {
            byte[][] packets = await ReadPacketsAsync(1);
            MediaBufferAllocator allocator = new(256 * 1024);
            using MediaPacket first = Packet(allocator, packets[0], 0);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(1, false), allocator);
            IPacketPrerollDecoder preroll = Assert.IsAssignableFrom<IPacketPrerollDecoder>(decoder);
            preroll.ResetForPreroll(0, false);
            Assert.Throws<NotSupportedException>(() => decoder.TrySend(first));
            Assert.Equal(first.Data.Length, allocator.AllocatedBytes);
            preroll.ResetForPreroll(1, true);
            decoder.TrySend(first);
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            using MediaPacket next = Packet(allocator, packets[1], 1);
            decoder.TrySend(next);
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(2048, frame.SamplesPerChannel);
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr");
        private static EncodedStreamInfo Stream(int channels, bool explicitSbr) => AacSbrDecodeTestInput.Stream(channels, explicitSbr);
        private static MediaPacket Packet(MediaBufferAllocator allocator, byte[] bytes, int index) => AacSbrDecodeTestInput.Packet(allocator, bytes, index);
        private static Task<byte[][]> ReadPacketsAsync(int channels) => AacSbrDecodeTestInput.ReadPacketsAsync(Root, channels);
    }
}
