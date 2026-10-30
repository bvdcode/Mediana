// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AvcHlsWriterTests
    {
        [Theory]
        [InlineData("avc-multislice-cabac", 96, 80, 18, 6)]
        [InlineData("avc-multislice-cabac", 48, 40, 18, 6)]
        [InlineData("avc-weighted", 48, 30, 12, 3)]
        public async Task CompleteRealPipelinesMatchEveryIndependentPictureAndTransportPacket(string name, int width, int height, int count, int segments) =>
            await HlsEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, width, height, count, segments);

        [Fact]
        public async Task VeryShortMediaHasNoInventedMasterPeak()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            using VideoFrame first = H264EncoderTests.Frame(format, allocator, 0);
            encoder.TrySend(first);
            using MediaPacket packet = encoder.Receive().Value!;
            int opened = 0;
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, (_, _) =>
            {
                opened++;
                return ValueTask.FromResult<Stream>(new MemoryStream());
            }, allocator, new() { TargetDuration = new(1, new(1, 100)), MaximumSegments = 1 });
            await writer.WritePacketAsync(packet);
            await writer.CompleteAsync();
            Assert.Throws<NotSupportedException>(() => writer.CreateVariant("v.m3u8"));
            Assert.Contains("#EXTINF:0.04,\n", writer.CreateVodPlaylist(_ => "s.ts"));
            Assert.Equal(1, opened);
        }

        [Fact]
        public async Task SegmentLimitRejectsTheBoundaryBeforeOpeningItsDestination()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            int opened = 0;
            HlsTestDestination destination = new();
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, (_, _) =>
            {
                opened++;
                return ValueTask.FromResult<Stream>(destination);
            }, allocator, new() { TargetDuration = new(1, new(1, 5)), MaximumSegments = 1 });
            using VideoFrame first = H264EncoderTests.Frame(format, allocator, 0);
            encoder.TrySend(first);
            using MediaPacket packet = encoder.Receive().Value!;
            await writer.WritePacketAsync(packet);
            using VideoFrame boundary = H264EncoderTests.Frame(format, allocator, 5);
            encoder.TrySend(boundary);
            using MediaPacket next = encoder.Receive().Value!;
            await Assert.ThrowsAsync<NotSupportedException>(() => writer.WritePacketAsync(next).AsTask());
            Assert.Equal(1, opened);
            await writer.DisposeAsync();
            Assert.True(destination.Disposed);
        }

        [Fact]
        public async Task StreamsExactPacketsIntoOwnedNonseekableSegmentsWithContinuousTransportState()
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            List<HlsTestDestination> destinations = [];
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, (number, _) =>
            {
                Assert.Equal(destinations.Count, number);
                HlsTestDestination destination = new();
                destinations.Add(destination);
                return ValueTask.FromResult<Stream>(destination);
            }, allocator, new() { TargetDuration = new(1, new(1, 5)) });
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            for (int index = 0; index < 25; index++)
            {
                using VideoFrame input = H264EncoderTests.Frame(format, allocator, index);
                encoder.TrySend(input);
                using MediaPacket packet = encoder.Receive().Value!;
                long pts = packet.PresentationTime!.Value.Rescale(new(1, 90000), TimestampRounding.NearestEven).Value + 90000;
                expected.Add((0, packet.Data.ToArray(), pts, pts));
                await writer.WritePacketAsync(packet);
            }
            Assert.Throws<InvalidOperationException>(() => writer.CreateVodPlaylist(_ => "segment.ts"));
            await writer.CompleteAsync();
            await writer.CompleteAsync();
            Assert.True(writer.IsComplete);
            Assert.Equal(5, writer.Segments.Count);
            Assert.All(destinations, destination => Assert.True(destination.Disposed));
            byte[] all = destinations.SelectMany(destination => destination.ToArray()).ToArray();
            Assert.Equal(all.LongLength, writer.BytesWritten);
            Assert.Equal(all.LongLength, writer.Segments.Sum(segment => segment.ByteLength));
            Assert.All(writer.Segments, segment => Assert.Equal(new MediaDuration(200, new(1, 1000)), segment.Duration));
            MpegTsTestInput.Verify(all, [encoder.Stream], expected);
            await writer.DisposeAsync();
            string playlist = writer.CreateVodPlaylist(number => $"segment-{number}.ts");
            Assert.Contains("#EXT-X-TARGETDURATION:1\n", playlist);
            Assert.Contains("#EXT-X-INDEPENDENT-SEGMENTS\n", playlist);
            Assert.Equal(5, playlist.Split("#EXTINF:0.2,\n").Length - 1);
            HlsVariant variant = writer.CreateVariant("video/index.m3u8", 16, 12);
            Assert.Equal("avc1.42C029", variant.Codecs);
            Assert.Equal(all.LongLength * 8, variant.AverageBandwidth);
            long peak = 0;
            for (int first = 0; first < 5; first++)
            {
                for (int count = 3; first + count <= 5; count++)
                {
                    long bytes = writer.Segments.Skip(first).Take(count).Sum(segment => segment.ByteLength);
                    peak = Math.Max(peak, (long)decimal.Ceiling(bytes * 8m / (count * 0.2m)));
                }
            }
            Assert.Equal(peak, variant.Bandwidth);
            string master = HlsMasterPlaylist.Create([variant]);
            Assert.Contains("CODECS=\"avc1.42C029\",RESOLUTION=16x12\nvideo/index.m3u8\n", master);
            encoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FailureOrCancellationFaultsTheWriterAndDisposalReleasesItsDestination(bool failure)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            encoder.TrySend(frame);
            using MediaPacket packet = encoder.Receive().Value!;
            HlsTestDestination destination = new() { FailWrites = failure };
            AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, (_, _) => ValueTask.FromResult<Stream>(destination), allocator);
            if (failure)
            {
                await Assert.ThrowsAsync<IOException>(() => writer.WritePacketAsync(packet).AsTask());
            }
            else
            {
                await writer.WritePacketAsync(packet);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.CompleteAsync(new(true)).AsTask());
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WritePacketAsync(packet).AsTask());
            await writer.DisposeAsync();
            Assert.True(destination.Disposed);
            Assert.False(writer.IsComplete);
            Assert.Throws<InvalidOperationException>(() => writer.CreateVodPlaylist(_ => "s.ts"));
            packet.Dispose();
            frame.Dispose();
            encoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task OverlappingCallsCannotMutateTheActiveWriter()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            encoder.TrySend(frame);
            using MediaPacket packet = encoder.Receive().Value!;
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            HlsTestDestination destination = new() { ReleaseWrites = release };
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, (_, _) => ValueTask.FromResult<Stream>(destination), allocator);
            Task pending = writer.WritePacketAsync(packet).AsTask();
            await destination.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.DisposeAsync().AsTask());
            release.SetResult();
            await pending;
            await writer.CompleteAsync();
            Assert.Single(writer.Segments);
        }
    }
}
