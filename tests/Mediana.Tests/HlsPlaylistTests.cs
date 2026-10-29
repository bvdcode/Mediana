// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Globalization;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsPlaylistTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("#EXT-X-ENDLIST")]
        [InlineData("segment.ts\n#EXT-X-ENDLIST")]
        [InlineData("segment.ts\r")]
        [InlineData("segment file.ts")]
        [InlineData("segment%zz.ts")]
        public async Task InvalidUriLinesCannotInjectPlaylistContent(string uri)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await using Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0);
            Assert.Throws<ArgumentException>(() => session.CreateVodPlaylist(_ => uri));
            Assert.Throws<ArgumentNullException>(() => session.CreateVodPlaylist(null!));
        }

        [Fact]
        public async Task UriLengthLimitAndInvariantDurationsRemainUsableAfterDisposal()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            Mp4HlsOptions options = HlsSessionTestInput.Options() with { MaximumSegmentUriCharacters = 8 };
            Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, options);
            await session.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                string playlist = session.CreateVodPlaylist(number => $"s{number}.ts");
                Assert.Contains("#EXTINF:1.2,\n", playlist);
                Assert.DoesNotContain("\r", playlist);
                Assert.DoesNotContain("INDEPENDENT-SEGMENTS", playlist);
                Assert.DoesNotContain("DISCONTINUITY", playlist);
                Assert.Throws<ArgumentException>(() => session.CreateVodPlaylist(_ => "length.ts"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public async Task FinalPlaylistDurationIncludesSelectedAudioTailOnTheTransportClock()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-segments.mp4");
            int edits = data.AsSpan().LastIndexOf("elst"u8) + 4;
            Assert.Equal(0, data[edits]);
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(edits + 4)));
            Assert.Equal(1024, BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(edits + 12)));
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(edits + 12), 0);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, HlsSessionTestInput.Options());
            Assert.Equal(new MediaDuration(109920, new TimeBase(1, 90000)), session.Segments[2].Duration);
            Assert.Equal(new MediaDuration(12288, new TimeBase(1, 10240)), session.Segments[2].Video.Duration);
            Assert.Contains("#EXTINF:1.221333333333333,\n", session.CreateVodPlaylist(number => $"s{number}.ts"));
        }
    }
}
