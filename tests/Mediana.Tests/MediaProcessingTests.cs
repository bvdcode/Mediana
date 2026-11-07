// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class MediaProcessingTests
    {
        [Fact]
        public async Task AudioWarmupAndEncodedPacketSelectionUseIndependentIntervalsOnTheSameClock()
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-aac.mp4"));
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                long baseline = allocator.AllocatedBytes;
                MediaTimeRange input = new(new(100, new(1, 1000)), new(900, new(1, 1000)));
                MediaTimeRange output = new(new(300, new(1, 1000)), new(500, new(1, 1000)));
                await using (MediaProcessingSession session = await Engine().OpenProcessingAsync(reader,
                    [new AudioEncodingRequest(1, 13, new AacEncoderFactory(), selection: input, packetSelection: output)],
                    allocator, new() { Selection = output }))
                {
                    await using ProcessingPacketWriter writer = new();
                    MediaProcessingResult result = await session.WriteAsync(writer);
                    Assert.Equal(9, result.Packets);
                    Assert.Equal(new MediaTimestamp(15040, new(1, 48000)), writer.Packets[0].Pts);
                    Assert.All(writer.Packets, packet => Assert.True(packet.Pts!.Value.CompareTo(output.Start) >= 0 && packet.Pts.Value.CompareTo(output.End) < 0));
                    Assert.InRange(result.Tracks[0].SamplesPerChannel, 20000, 38399);
                }
                Assert.Equal(baseline, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WholeAudiovisualOperationPreservesSamplesAndMergesDelayOnOriginalClocks(bool copyVideo)
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-aac.mp4"));
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                long baseline = allocator.AllocatedBytes;
                MediaTrackRequest video = copyVideo ? new PacketCopyRequest(0, 7) : new VideoEncodingRequest(0, 7,
                    new H264EncoderFactory(), new VideoPreparationOptions { MaximumWidth = 32, MaximumHeight = 32 });
                MediaEngine engine = Engine();
                await using (MediaProcessingSession session = await engine.OpenProcessingAsync(reader,
                    [video, new AudioEncodingRequest(1, 13, new AacEncoderFactory())], allocator))
                {
                    await using ProcessingPacketWriter writer = new();
                    MediaProcessingResult result = await session.WriteAsync(writer);
                    Assert.True(writer.Complete);
                    Assert.False(writer.Disposed);
                    MediaTrackResult audio = Assert.Single(result.Tracks, track => track.Stream.Kind == MediaKind.Audio);
                    MediaTrackResult pictures = Assert.Single(result.Tracks, track => track.Stream.Kind == MediaKind.Video);
                    Assert.Equal(76800, audio.SamplesPerChannel);
                    Assert.Equal(1024, audio.PrimingSamples);
                    Assert.Equal(0, audio.PaddingSamples);
                    Assert.Equal(copyVideo ? 0 : 8, pictures.Frames);
                    Assert.Equal(writer.Packets.Count, result.Packets);
                    Assert.Equal(new[] { 7, 13 }, session.Streams.Select(stream => stream.StreamIndex));
                    for (int index = 1; index < writer.Packets.Count; index++)
                    { Assert.True(writer.Packets[index].Dts!.Value.CompareTo(writer.Packets[index - 1].Dts!.Value) >= 0); }
                    Assert.Equal(new MediaTimestamp(-1024, new(1, 48000)), writer.Packets.First(packet => packet.Stream == 13).Dts);
                    if (copyVideo)
                    {
                        byte[][] copied = writer.Packets.Where(packet => packet.Stream == 7).Select(packet => packet.Data).ToArray();
                        for (int index = 0; index < copied.Length; index++)
                        {
                            using MediaPacket original = await reader.ReadPacketAtAsync(0, index);
                            Assert.Equal(original.Data.ToArray(), copied[index]);
                        }
                    }
                    await Assert.ThrowsAsync<InvalidOperationException>(() => session.WriteAsync(writer));
                }
                Assert.Equal(baseline, allocator.AllocatedBytes);
                using MediaPacket first = (await reader.ReadPacketAsync())!;
                Assert.Equal(reader.GetSampleInfo(first.StreamIndex, 0).PresentationTime, first.PresentationTime);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task ArbitraryAudiovisualIntervalsTrimPcmExactlyAcrossRepeatedRequests(int window)
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-aac.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            MediaTimeRange interval = new(new(window * 500 + 333, new(1, 1000)), new(window * 500 + 777, new(1, 1000)));
            MediaProcessingOptions options = new() { Selection = interval };
            long baseline = allocator.AllocatedBytes;
            foreach (int repeat in new[] { 0, 1 })
            {
                await using (MediaProcessingSession session = await Engine().OpenProcessingAsync(reader,
                    [new VideoEncodingRequest(0, 7, new H264EncoderFactory()), new AudioEncodingRequest(1, 13, new AacEncoderFactory())], allocator, options))
                {
                    await using ProcessingPacketWriter writer = new();
                    MediaProcessingResult result = await session.WriteAsync(writer);
                    MediaTrackResult audio = result.Tracks[1];
                    Assert.Equal(21312, audio.SamplesPerChannel);
                    Assert.Equal((1024 - 21312 % 1024) % 1024, audio.PaddingSamples);
                    Assert.Equal(interval.Start, new MediaTimestamp(writer.Packets.First(packet => packet.Stream == 13).Pts!.Value
                        .Rescale(new(1, 48000), TimestampRounding.TowardZero).Value + 1024, new(1, 48000)));
                    ProcessingPacket[] videos = writer.Packets.Where(packet => packet.Stream == 7).ToArray();
                    Assert.All(videos, packet => Assert.True(packet.Pts!.Value.CompareTo(interval.Start) >= 0 && packet.Pts.Value.CompareTo(interval.End) < 0));
                    ProcessingPacket last = videos[^1];
                    TimeBase clock = TimeBase.Common(last.Pts!.Value.TimeBase, last.Duration!.Value.TimeBase);
                    Assert.Equal(interval.End, new MediaTimestamp(last.Pts.Value.Rescale(clock, TimestampRounding.TowardZero).Value +
                        new MediaTimestamp(last.Duration.Value.Value, last.Duration.Value.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value, clock));
                }
                Assert.Equal(baseline, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task ColdAudiovisualTailRequestsReadOnlyRequiredPayloadAndRepeatIdentically()
        {
            byte[] bytes = await Mp4TestData.ReadAsync("avc-aac-on-demand.mp4");
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            long baseline = allocator.AllocatedBytes;
            MediaTimeRange range = new(new(11, new(1, 1)), new(12, new(1, 1)));
            byte[][]? previous = null;
            for (int repeat = 0; repeat < 2; repeat++)
            {
                source.Reads.Clear();
                await using (MediaProcessingSession session = await Engine().OpenProcessingAsync(reader,
                    [new VideoEncodingRequest(0, 7, new H264EncoderFactory(), outputTimeBase: new(1, 5)),
                     new AudioEncodingRequest(1, 13, new AacEncoderFactory())], allocator, new() { Selection = range }))
                {
                    await using ProcessingPacketWriter writer = new();
                    MediaProcessingResult result = await session.WriteAsync(writer);
                    Assert.Equal(5, result.Tracks[0].Frames);
                    Assert.Equal(48000, result.Tracks[1].SamplesPerChannel);
                    Assert.Equal(128, result.Tracks[1].PaddingSamples);
                    Assert.True(source.Reads.Sum(read => read.Length) < bytes.Length / 2);
                    long videoStart = reader.GetSampleInfo(0, 50).Offset;
                    Assert.All(source.Reads, read => Assert.True(read.Offset >= videoStart));
                    byte[][] payloads = writer.Packets.Select(packet => packet.Data).ToArray();
                    if (previous is not null) { Assert.Equal(previous, payloads); }
                    previous = payloads;
                }
                Assert.Equal(baseline, allocator.AllocatedBytes);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task CancellationLimitsAndFailureReleaseAllTracksWithoutOwningReader(int failure)
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-aac.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            long baseline = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            if (failure == 0) { await cancellation.CancelAsync(); }
            MediaProcessingOptions options = new() { Input = new() { MaxFrameReads = failure == 1 ? 2 : null } };
            async Task RunAsync()
            {
                await using MediaProcessingSession session = await Engine().OpenProcessingAsync(reader,
                    [new VideoEncodingRequest(0, 7, new H264EncoderFactory()), new AudioEncodingRequest(1, 13, new AacEncoderFactory())], allocator, options, cancellation.Token);
                await using ProcessingPacketWriter writer = new() { OnWrite = () => throw new IOException("Write failure.") };
                await session.WriteAsync(writer, cancellation.Token);
            }
            if (failure == 0) { await Assert.ThrowsAnyAsync<OperationCanceledException>(RunAsync); }
            else if (failure == 1) { await Assert.ThrowsAsync<InvalidOperationException>(RunAsync); }
            else { await Assert.ThrowsAsync<IOException>(RunAsync); }
            Assert.Equal(baseline, allocator.AllocatedBytes);
            using MediaPacket first = await reader.ReadPacketAtAsync(0, 0);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task FullFacadeWritesARealTransportStreamUsingTheSameTrackContracts()
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-aac.mp4"));
            using MemoryStream destination = new();
            MediaProcessingResult result = await Engine().ProcessAsync(source, destination, "mpegts",
                [new VideoEncodingRequest(0, 7, new H264EncoderFactory()), new AudioEncodingRequest(1, 13, new AacEncoderFactory())], allocator);
            Assert.Equal(2, result.Tracks.Count);
            Assert.True(destination.Length > 0 && destination.Length % 188 == 0);
            for (int offset = 0; offset < destination.Length; offset += 188) { Assert.Equal(0x47, destination.GetBuffer()[offset]); }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        private static MediaEngine Engine() => new MediaEngineBuilder()
            .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            .AddVideoDecoder(new H264DecoderFactory()).AddAudioDecoder(new AacDecoderFactory())
            .AddContainerWriter(new MpegTsWriterFactory()).Build();

        [Theory]
        [InlineData("FrameRate", "frame-rate-cfr.mp4")]
        [InlineData("FrameRate", "frame-rate-vfr.mp4")]
        [InlineData("FrameRate", "frame-rate-edited.mp4")]
        [InlineData("Matroska", "avc-aac-attachment.mkv")]
        public async Task ExactPresentationProcessingWorksAcrossIndexedContainerClocks(string group, string file)
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", group, file)));
            await using IIndexedContainerReader reader = group switch
            {
                "FrameRate" => await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }),
                "Matroska" => await MatroskaReader.OpenAsync(source, allocator),
                _ => throw new ArgumentOutOfRangeException(nameof(group))
            };
            EncodedStreamInfo stream = reader.Streams.First(item => item.Kind == MediaKind.Video);
            _ = VideoSegmentPlanner.GetTimeBase((IIndexedPacketTimingReader)reader, stream.StreamIndex);
            long baseline = allocator.AllocatedBytes;
            await using (MediaProcessingSession session = await Engine().OpenProcessingAsync(reader,
                [new VideoEncodingRequest(stream.StreamIndex, 17, new H264EncoderFactory())], allocator))
            {
                await using ProcessingPacketWriter writer = new();
                MediaProcessingResult result = await session.WriteAsync(writer);
                Assert.True(result.Tracks[0].Frames > 0);
                for (int index = 1; index < writer.Packets.Count; index++)
                { Assert.True(writer.Packets[index].Pts!.Value.CompareTo(writer.Packets[index - 1].Pts!.Value) > 0); }
            }
            Assert.Equal(baseline, allocator.AllocatedBytes);
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
