// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AvcAudioHlsTests
    {
        [Theory]
        [InlineData("avc-aac.mp4", "mp4a.40.2")]
        [InlineData("avc-mp3-44.mp4", "mp4a.6B")]
        [InlineData("avc-mp3-22.mp4", "mp4a.69")]
        public Task CompletePresentationPreservesAllAudioAndEncodedVideoPackets(string name, string codecs) =>
            AvcAudioHlsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, codecs);

        [Fact]
        public async Task AudioAtTheSameTimestampMustFollowVideoBeforeOpeningAnyDestination()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            EncodedStreamInfo audio = new(1, MediaKind.Audio, "aac", new(1, 44100), [0x12, 0x08]);
            int opened = 0;
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, audio, (_, _) =>
            {
                opened++;
                return ValueTask.FromResult<Stream>(new MemoryStream());
            }, allocator);
            using WritableMediaBuffer storage = allocator.Allocate(1);
            storage.Memory.Span[0] = 0;
            using MediaBuffer data = storage.Publish();
            using MediaPacket sound = new(1, data, new(0, audio.TimeBase), new(0, audio.TimeBase), new(1024, audio.TimeBase));
            await writer.WritePacketAsync(sound);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            encoder.TrySend(frame);
            using MediaPacket video = encoder.Receive().Value!;
            long bytes = writer.BytesWritten;
            await Assert.ThrowsAsync<InvalidDataException>(() => writer.WritePacketAsync(video).AsTask());
            Assert.Equal(bytes, writer.BytesWritten);
            Assert.Equal(1, opened);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
        }

        [Fact]
        public async Task MissingConfiguredAudioCannotProduceACompletePlaylist()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame frame = H264EncoderTests.Frame(H264EncoderTests.Format(16, 16), allocator, 0);
            using H264Encoder encoder = H264Encoder.Create(frame.Format, new(1, 1000), allocator);
            EncodedStreamInfo audio = new(1, MediaKind.Audio, "aac", new(1, 44100), [0x12, 0x08]);
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, audio, (_, _) => ValueTask.FromResult<Stream>(new MemoryStream()), allocator);
            encoder.TrySend(frame);
            using MediaPacket packet = encoder.Receive().Value!;
            await writer.WritePacketAsync(packet);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            Assert.False(writer.IsComplete);
        }

        [Theory]
        [InlineData("flac", 44100)]
        [InlineData("aac", 2147483647)]
        public void UnsupportedAudioOrUnrepresentableCommonClockIsRejectedWithoutOpeningOutput(string codec, int denominator)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Encoder encoder = H264Encoder.Create(H264EncoderTests.Format(16, 16), new(1, 1000), allocator);
            EncodedStreamInfo audio = new(1, MediaKind.Audio, codec, new(1, denominator), [0x12, 0x08]);
            Assert.Throws<NotSupportedException>(() => AvcHlsWriter.Create(encoder.Stream, audio,
                (_, _) => throw new InvalidOperationException("No destination should be opened."), allocator));
        }
    }
}
