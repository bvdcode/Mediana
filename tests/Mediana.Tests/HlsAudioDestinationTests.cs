// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsAudioDestinationTests
    {
        [Fact]
        public async Task EncodedRatesWriteCompleteContinuousAudioPlaylistsWithoutVideoDestinations()
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource input = new(await File.ReadAllBytesAsync(Data("avc-aac.mp4")));
            await using HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(input, 1,
                [96000, 128000, 96000], null, allocator, default);
            List<MemoryStream>[] segments = [[], [], []];
            AudioHlsWriter[] writers = Enumerable.Range(0, segments.Length)
                .Select(index => Create(audio.GetStream(index), segments[index], allocator)).ToArray();
            try
            {
                await audio.WriteBeforeAsync(new(0, new(1, 48000)), writers, default);
                Assert.Equal(2, audio.PacketsWritten);
                await audio.WriteBeforeAsync(new(0, new(1, 48000)), writers, default);
                Assert.Equal(2, audio.PacketsWritten);
                await audio.WriteBeforeAsync(null, writers, default);
                Assert.Equal(152, audio.PacketsWritten);
                await audio.DisposeAsync();
                Assert.All(writers, writer => Assert.False(writer.IsComplete));
                foreach (AudioHlsWriter writer in writers)
                {
                    await writer.CompleteAsync();
                }
                for (int variant = 0; variant < 2; variant++)
                {
                    await using HlsAacAudioSource reference = await HlsAacAudioSource.OpenAsync(input, 1,
                        [variant == 0 ? 96000 : 128000], null, allocator, default);
                    CollectingPacketWriter packets = new();
                    await reference.WriteBeforeAsync(null, [packets], default);
                    List<(int Stream, byte[] Data, long Pts, long Dts)> expected = packets.Packets
                        .Select((data, index) => (1, data,
                            MpegTsTestInput.Ticks(new(-1024 + index * 1024, reference.Stream.TimeBase)),
                            MpegTsTestInput.Ticks(new(-1024 + index * 1024, reference.Stream.TimeBase)))).ToList();
                    Verify(writers[variant], segments[variant], reference.Stream, expected);
                    Assert.False(packets.IsComplete);
                    Assert.False(packets.IsDisposed);
                }
                Assert.Equal(segments[0].SelectMany(segment => segment.ToArray()).ToArray(),
                    segments[2].SelectMany(segment => segment.ToArray()).ToArray());
            }
            finally
            {
                foreach (AudioHlsWriter writer in writers)
                {
                    await writer.DisposeAsync();
                }
            }
            Assert.False(input.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("avc-aac.mp4")]
        [InlineData("avc-mp3-44.mp4")]
        [InlineData("avc-mp3-22.mp4")]
        public async Task CopiedAudioKeepsEveryPayloadAndTimestampInAudioOnlyDestinations(string file)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource input = new(await File.ReadAllBytesAsync(Data(file)));
            await using HlsCopyAudioSource audio = await HlsCopyAudioSource.OpenAsync(input, 1, allocator, default);
            List<MemoryStream> segments = [];
            await using AudioHlsWriter writer = Create(audio.Stream, segments, allocator);
            CollectingPacketWriter copy = new();
            await audio.WriteBeforeAsync(new(0, new(1, 1)), [writer, copy], default);
            await audio.WriteBeforeAsync(null, [writer, copy], default);
            await audio.DisposeAsync();
            Assert.False(writer.IsComplete);
            Assert.False(copy.IsComplete);
            Assert.False(copy.IsDisposed);
            await writer.CompleteAsync();
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            await using (Mp4Reader reference = await Mp4Reader.OpenAsync(input, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                for (int sample = 0; sample < reference.GetPacketCount(1); sample++)
                {
                    using MediaPacket packet = await reference.ReadPacketAtAsync(1, sample);
                    expected.Add((1, packet.Data.ToArray(), MpegTsTestInput.Ticks(packet.PresentationTime),
                        MpegTsTestInput.Ticks(packet.DecodeTime)));
                    Assert.Equal(packet.Data.ToArray(), copy.Packets[sample]);
                }
            }
            Assert.Equal(expected.Count, audio.PacketsWritten);
            Assert.Equal(expected.Count, copy.Packets.Count);
            Verify(writer, segments, audio.Stream, expected);
            await writer.DisposeAsync();
            Assert.False(input.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FailedDestinationTerminatesEncodingAndLeavesBorrowedWritersOpen()
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource input = new(await File.ReadAllBytesAsync(Data("avc-aac.mp4")));
            await using HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(input, 1,
                [96000, 96000], null, allocator, default);
            CollectingPacketWriter first = new();
            CollectingPacketWriter failing = new() { FailWrites = true };
            IContainerWriter[] destinations = [first, failing];
            await Assert.ThrowsAsync<IOException>(async () => await audio.WriteBeforeAsync(null, destinations, default));
            Assert.Single(first.Packets);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await audio.WriteBeforeAsync(null, destinations, default));
            await audio.DisposeAsync();
            Assert.All(new[] { first, failing }, writer =>
            {
                Assert.False(writer.IsComplete);
                Assert.False(writer.IsDisposed);
            });
            Assert.False(input.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static AudioHlsWriter Create(EncodedStreamInfo stream, List<MemoryStream> segments, MediaBufferAllocator allocator)
            => AudioHlsWriter.Create(stream, (number, token) =>
            {
                token.ThrowIfCancellationRequested();
                Assert.Equal(segments.Count, number);
                MemoryStream destination = new();
                segments.Add(destination);
                return ValueTask.FromResult<Stream>(destination);
            }, allocator, new() { TargetDuration = new(1, new(1, 4)) });

        private static void Verify(AudioHlsWriter writer, List<MemoryStream> segments, EncodedStreamInfo stream,
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected)
        {
            Assert.True(writer.IsComplete);
            Assert.True(segments.Count > 1);
            Assert.Equal(segments.Count, writer.Segments.Count);
            Assert.All(segments, segment => Assert.False(segment.CanWrite));
            Assert.EndsWith("#EXT-X-ENDLIST\n", writer.CreateVodPlaylist(number => $"audio-{number}.ts"), StringComparison.Ordinal);
            MpegTsTestInput.Verify(segments.SelectMany(segment => segment.ToArray()).ToArray(), [stream], expected);
        }

        private static string Data(string file) => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", file);
    }
}
