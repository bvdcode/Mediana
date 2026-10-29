// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Mp3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class Mp3DecoderTests
    {
        [Fact]
        public async Task RetainedOutputSurvivesDrainResetAndDisposal()
        {
            byte[] data = await FirstPacketAsync();
            MediaBufferAllocator allocator = new(128 * 1024);
            AudioFrame? retained = null;
            try
            {
                using MediaPacket packet = Packet(allocator, data);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new Mp3DecoderFactory().Create(Stream(data), allocator);
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
                decoder.CompleteInput();
                retained = decoder.Receive().Value;
                byte[] expected = retained.GetPlane(0).ToArray();
                Assert.Equal(1152, retained.SamplesPerChannel);
                Assert.Equal(new MediaTimestamp(123, new TimeBase(1, 48000)), retained.Timestamp);
                Assert.Equal(new[] { "front-left", "front-right" }, retained.Format.Channels);
                Assert.Equal(data.Length + expected.Length, allocator.AllocatedBytes);
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                decoder.Reset();
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                using AudioFrame repeated = decoder.Receive().Value;
                Assert.Equal(expected, repeated.GetPlane(0).ToArray());
                decoder.Dispose();
                Assert.Equal(expected, retained.GetPlane(0).ToArray());
                Assert.Equal(data, packet.Data.ToArray());
                Assert.Throws<ObjectDisposedException>(() => decoder.Receive());
            }
            finally
            {
                retained?.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ResetRestoresAllFilterHistoryAndMalformedInputFaults()
        {
            byte[] data = await FirstPacketAsync();
            MediaBufferAllocator allocator = new(128 * 1024);
            using MediaPacket packet = Packet(allocator, data);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new Mp3DecoderFactory().Create(Stream(data), allocator);
            decoder.TrySend(packet);
            using AudioFrame first = decoder.Receive().Value;
            byte[] expected = first.GetPlane(0).ToArray();
            decoder.TrySend(packet);
            using AudioFrame following = decoder.Receive().Value;
            Assert.NotEqual(expected, following.GetPlane(0).ToArray());
            using MediaPacket truncated = Packet(allocator, data[..^1]);
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(truncated));
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            Assert.Equal(data.Length + truncated.Data.Length + 2 * expected.Length, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(packet);
            using AudioFrame repeated = decoder.Receive().Value;
            Assert.Equal(expected, repeated.GetPlane(0).ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CancellationReleasesPendingOutputAndRequiresReset(bool receive)
        {
            byte[] data = await FirstPacketAsync();
            MediaBufferAllocator allocator = new(128 * 1024);
            using MediaPacket packet = Packet(allocator, data);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new Mp3DecoderFactory().Create(Stream(data), allocator);
            decoder.TrySend(packet);
            using CancellationTokenSource source = new();
            await source.CancelAsync();
            if (receive)
            {
                Assert.Throws<OperationCanceledException>(() => decoder.Receive(source.Token));
            }
            else
            {
                Assert.Throws<OperationCanceledException>(() => decoder.TrySend(packet, source.Token));
            }
            Assert.Equal(data.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.CompleteInput());
            decoder.Reset();
            decoder.TrySend(packet);
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(1152, frame.SamplesPerChannel);
        }

        [Theory]
        [InlineData(33000)]
        [InlineData(42000)]
        [InlineData(48000)]
        public async Task FailedWorkspaceReservoirOrOutputAllocationReleasesAllStorage(int budget)
        {
            byte[] data = await FirstPacketAsync();
            MediaBufferAllocator allocator = new(budget);
            using MediaPacket packet = Packet(allocator, data);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new Mp3DecoderFactory().Create(Stream(data), allocator);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
            Assert.Equal(data.Length, allocator.AllocatedBytes);
            decoder.Reset();
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
        }

        [Fact]
        public async Task ConfigurationLimitEmphasisAndFormatChangesAreExplicit()
        {
            byte[] data = await FirstPacketAsync();
            Assert.NotNull(new Mp3DecoderFactory(4).GetUnsupportedReason(Stream(data)));
            byte[] emphasized = data.ToArray();
            emphasized[3] |= 1;
            Assert.NotNull(new Mp3DecoderFactory().GetUnsupportedReason(Stream(emphasized)));
            MediaBufferAllocator allocator = new(128 * 1024);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new Mp3DecoderFactory().Create(Stream(data), allocator);
            byte[] changed = await FirstPacketAsync("vector-44100-2");
            using MediaPacket packet = Packet(allocator, changed);
            Assert.Throws<NotSupportedException>(() => decoder.TrySend(packet));
            Assert.Equal(changed.Length, allocator.AllocatedBytes);
        }

        private static async Task<byte[]> FirstPacketAsync(string name = "vector-48000-2")
        {
            byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3", name + ".mp3"));
            return file[..Mp3FrameHeader.Read(file.AsSpan(0, 4)).FrameBytes];
        }

        private static EncodedStreamInfo Stream(byte[] data) => new(0, MediaKind.Audio, "mp3", new TimeBase(1, 48000), data.AsSpan(0, 4));

        private static MediaPacket Packet(MediaBufferAllocator allocator, byte[] data)
        {
            using WritableMediaBuffer writable = allocator.Allocate(data.Length);
            data.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new MediaPacket(0, buffer, new MediaTimestamp(123, new TimeBase(1, 48000)));
        }
    }
}
