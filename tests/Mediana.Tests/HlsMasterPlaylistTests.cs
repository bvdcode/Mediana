// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsMasterPlaylistTests
    {
        [Fact]
        public void VariantsPreserveOrderAndOptionalDisplayResolution()
        {
            string text = HlsMasterPlaylist.Create([
                new("large/index.m3u8", "avc1.42C029", 400000, 300000, 640, 360),
                new("small/index.m3u8", "avc1.42c029", 200000, 150000)]);
            Assert.Equal("#EXTM3U\n#EXT-X-VERSION:3\n" +
                "#EXT-X-STREAM-INF:BANDWIDTH=400000,AVERAGE-BANDWIDTH=300000,CODECS=\"avc1.42C029\",RESOLUTION=640x360\nlarge/index.m3u8\n" +
                "#EXT-X-STREAM-INF:BANDWIDTH=200000,AVERAGE-BANDWIDTH=150000,CODECS=\"avc1.42c029\"\nsmall/index.m3u8\n", text);
        }

        [Theory]
        [InlineData("line\n#EXT-X-ENDLIST", "avc1.42C029", 1, 1, 2, 2)]
        [InlineData("s.m3u8", "avc1.42C02G", 1, 1, 2, 2)]
        [InlineData("s.m3u8", "avc1.42C029\"", 1, 1, 2, 2)]
        [InlineData("s.m3u8", "avc1.42C029,mp4a.40.2\"", 1, 1, 2, 2)]
        [InlineData("s.m3u8", "avc1.42C029,mp4a.40.5", 1, 1, 2, 2)]
        [InlineData("s.m3u8", "avc1.42C029,mp4a.6B,mp4a.69", 1, 1, 2, 2)]
        [InlineData("s.m3u8", "avc1.42C029", 0, 1, 2, 2)]
        [InlineData("s.m3u8", "avc1.42C029", 1, -1, 2, 2)]
        [InlineData("s.m3u8", "avc1.42C029", 1, 1, 0, 2)]
        [InlineData("s.m3u8", "avc1.42C029", 1, 1, 2, null)]
        public void InvalidVariantCannotInjectOrMisstateRequiredMetadata(string uri, string codec, long peak, long average, int? width, int? height)
        {
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([new(uri, codec, peak, average, width, height)]));
        }

        [Fact]
        public void EmptyOrDuplicateVariantsAreRejected()
        {
            HlsVariant variant = new("v.m3u8", "avc1.42C029", 1, 1);
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([]));
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([variant, variant]));
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([variant], 2));
        }

        [Theory]
        [InlineData("mp4a.40.2")]
        [InlineData("mp4a.6B")]
        [InlineData("mp4a.69")]
        public void SupportedAudioCodecsAreIncludedInTheVariant(string codec)
        {
            string text = HlsMasterPlaylist.Create([new("v.m3u8", "avc1.42C029," + codec, 10, 8)]);
            Assert.Contains("CODECS=\"avc1.42C029," + codec + "\"\n", text);
        }
    }
}
