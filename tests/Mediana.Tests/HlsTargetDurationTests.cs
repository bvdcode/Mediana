// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsTargetDurationTests
    {
        [Fact]
        public Task AudioPacketsCanShareTheVideoTargetWithoutClippingTheirCodedDuration() => HlsTargetDurationTestInput.VerifyAsync();

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void NonpositivePlaylistTargetsAreRejectedBeforeOpeningOutput(long target)
        {
            MediaBufferAllocator allocator = new(4096);
            EncodedStreamInfo audio = new(0, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 48000), [0x11, 0x88]);
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioHlsWriter.Create(audio,
                (_, _) => throw new InvalidOperationException("No output should be opened."), allocator,
                new() { PlaylistTargetDurationSeconds = target }));
        }

        [Theory]
        [InlineData(1499, true)]
        [InlineData(1500, false)]
        [InlineData(1501, false)]
        public async Task CommonTargetUsesExactNearestIntegerRounding(int milliseconds, bool accepted)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            encoder.TrySend(frame);
            using MediaPacket original = encoder.Receive().Value!;
            using MediaPacket packet = original.RetainWithDuration(new(milliseconds, new(1, 1000)));
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream,
                (_, _) => ValueTask.FromResult<Stream>(new MemoryStream()), allocator,
                new() { PlaylistTargetDurationSeconds = 1 });
            await writer.WritePacketAsync(packet);
            await writer.CompleteAsync();
            if (accepted)
            {
                Assert.Contains("#EXT-X-TARGETDURATION:1\n", writer.CreateVodPlaylist(_ => "video.ts"));
                Assert.True(writer.CreateVariant("video.m3u8").Bandwidth > 0);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => writer.CreateVodPlaylist(_ => "video.ts"));
                Assert.Throws<ArgumentException>(() => writer.CreateVariant("video.m3u8"));
            }
            Assert.Contains("#EXT-X-TARGETDURATION:2\n", writer.CreateVodPlaylist(_ => "video.ts", 2));
            Assert.Contains("#EXT-X-TARGETDURATION:2\n", writer.CreateVodPlaylist(_ => "video.ts", null));
            Assert.True(writer.CreateVariant("video.m3u8", null, null, 2).Bandwidth > 0);
            Assert.Equal(writer.CreateVariant("video.m3u8", null, null, 2), writer.CreateVariant("video.m3u8", null, null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.CreateVodPlaylist(_ => "video.ts", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.CreateVariant("video.m3u8", null, null, -1));
            Assert.Equal(new MediaDuration(milliseconds, new(1, 1000)), Assert.Single(writer.Segments).Duration);
        }

        [Fact]
        public async Task PeakBandwidthUsesTheAdvertisedTargetWindow()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator, new() { KeyFrameInterval = TimeSpan.Zero });
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream,
                (_, _) => ValueTask.FromResult<Stream>(new MemoryStream()), allocator,
                new() { TargetDuration = new(1, new(1, 5)), PlaylistTargetDurationSeconds = 2 });
            for (int index = 0; index < 25; index++)
            {
                using VideoFrame frame = H264EncoderTests.Frame(format, allocator, index);
                encoder.TrySend(frame);
                using MediaPacket packet = encoder.Receive().Value!;
                await writer.WritePacketAsync(packet);
            }
            await writer.CompleteAsync();
            Assert.Equal(5, writer.Segments.Count);
            Assert.Contains("#EXT-X-TARGETDURATION:2\n", writer.CreateVodPlaylist(number => $"{number}.ts"));
            HlsVariant variant = writer.CreateVariant("video.m3u8");
            Assert.Equal(writer.BytesWritten * 8, variant.AverageBandwidth);
            Assert.Equal(variant.AverageBandwidth, variant.Bandwidth);
            long peak = 0;
            for (int start = 0; start < writer.Segments.Count; start++)
            {
                long bytes = 0;
                long milliseconds = 0;
                for (int end = start; end < writer.Segments.Count; end++)
                {
                    HlsMediaSegment segment = writer.Segments[end];
                    Assert.Equal(new TimeBase(1, 1000), segment.Duration.TimeBase);
                    bytes += segment.ByteLength;
                    milliseconds += segment.Duration.Value;
                    if (milliseconds is >= 500 and <= 1500)
                    {
                        peak = Math.Max(peak, (bytes * 8000 + milliseconds - 1) / milliseconds);
                    }
                }
            }
            HlsVariant measured = writer.CreateVariant("video.m3u8", null, null, 1);
            Assert.Equal(peak, measured.Bandwidth);
            Assert.Equal(variant.AverageBandwidth, measured.AverageBandwidth);
            Assert.Contains("#EXT-X-TARGETDURATION:1\n", writer.CreateVodPlaylist(number => $"{number}.ts", 1));
            Assert.Contains("#EXT-X-TARGETDURATION:2\n", writer.CreateVodPlaylist(number => $"{number}.ts"));
        }
    }
}
