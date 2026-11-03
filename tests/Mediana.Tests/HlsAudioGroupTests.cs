// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsAudioGroupTests
    {
        [Fact]
        public void SelectionMetadataCodecUnionAndIndependentRateMaximaMatchTheReference() => HlsAudioGroupTestInput.Verify();

        [Theory]
        [InlineData("uri")]
        [InlineData("uri-quote")]
        [InlineData("group-empty")]
        [InlineData("group-quote")]
        [InlineData("name-empty")]
        [InlineData("name-quote")]
        [InlineData("name-control")]
        [InlineData("language-quote")]
        [InlineData("language-control")]
        [InlineData("codec")]
        [InlineData("peak")]
        [InlineData("average")]
        [InlineData("channels")]
        [InlineData("default")]
        public void InvalidAudioMetadataCannotInjectTagsOrMisstateRequiredValues(string scenario)
        {
            HlsAudioRendition valid = Audio();
            HlsAudioRendition invalid = scenario switch
            {
                "uri" => valid with { PlaylistUri = "audio\n#EXTM3U" },
                "uri-quote" => valid with { PlaylistUri = "audio\".m3u8" },
                "group-empty" => valid with { GroupId = "" },
                "group-quote" => valid with { GroupId = "audio\"" },
                "name-empty" => valid with { Name = "" },
                "name-quote" => valid with { Name = "Stereo\"" },
                "name-control" => valid with { Name = "Stereo\u0000" },
                "language-quote" => valid with { Language = "en\"" },
                "language-control" => valid with { Language = "en\n" },
                "codec" => valid with { Codecs = "mp4a.40.5" },
                "peak" => valid with { Bandwidth = 0 },
                "average" => valid with { AverageBandwidth = -1 },
                "channels" => valid with { Channels = 0 },
                "default" => valid with { IsDefault = true, AutoSelect = false },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([Video()], [invalid]));
        }

        [Fact]
        public void MissingGroupsMuxedAudioAndDuplicateVariantAssociationsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([Video()]));
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([Video() with { AudioGroupId = "other" }], [Audio()]));
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([Video() with { Codecs = "avc1.42C029,mp4a.40.2" }], [Audio()]));
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([Video(), Video()], [Audio()]));
        }

        [Fact]
        public void GroupNamesAndDefaultSelectionAreUnambiguous()
        {
            HlsAudioRendition first = Audio() with { IsDefault = true, AutoSelect = true };
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([Video()], [first, first with { PlaylistUri = "other.m3u8" }]));
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([Video()], [first, first with { Name = "Other", PlaylistUri = "other.m3u8" }]));
        }

        [Theory]
        [InlineData("name")]
        [InlineData("language")]
        [InlineData("default")]
        [InlineData("autoselect")]
        [InlineData("count")]
        public void MultipleAudioGroupsRequireCorrespondingSelectionAttributes(string scenario)
        {
            HlsAudioRendition first = Audio() with { Language = "en", AutoSelect = true };
            HlsAudioRendition second = first with { GroupId = "other", PlaylistUri = "other.m3u8" };
            second = scenario switch
            {
                "name" => second with { Name = "Other" },
                "language" => second with { Language = "fr" },
                "default" => second with { IsDefault = true },
                "autoselect" => second with { AutoSelect = false },
                "count" => second,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            List<HlsAudioRendition> audio = [first, second];
            if (scenario == "count")
            {
                audio.Add(second with { Name = "Other", PlaylistUri = "third.m3u8" });
            }
            Assert.Throws<ArgumentException>(() => HlsMasterPlaylist.Create([Video()], audio));
        }

        [Fact]
        public void SameVideoCanReferenceDifferentAudioEncodingsAndDuplicateCodecsAppearOnce()
        {
            HlsAudioRendition first = Audio() with { Language = "en", AutoSelect = true };
            HlsAudioRendition second = first with { Name = "Spanish", PlaylistUri = "es.m3u8", Language = "es" };
            HlsAudioRendition third = first with { GroupId = "surround", PlaylistUri = "six-en.m3u8", Channels = 6, Bandwidth = 200, AverageBandwidth = 150 };
            HlsAudioRendition fourth = second with { GroupId = "surround", PlaylistUri = "six-es.m3u8", Channels = 6, Bandwidth = 220, AverageBandwidth = 180 };
            string master = HlsMasterPlaylist.Create([Video(), Video() with { AudioGroupId = "surround" }], [first, second, third, fourth]);
            Assert.Contains("BANDWIDTH=500,AVERAGE-BANDWIDTH=380,CODECS=\"avc1.42C029,mp4a.40.2\",AUDIO=\"audio\"", master);
            Assert.Contains("BANDWIDTH=620,AVERAGE-BANDWIDTH=480,CODECS=\"avc1.42C029,mp4a.40.2\",AUDIO=\"surround\"", master);
            Assert.Equal(2, master.Split('\n').Count(line => line == "video.m3u8"));
            Assert.Contains("CHANNELS=\"6\"", master);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AggregateBandwidthOverflowIsExplicit(bool peak)
        {
            HlsVariant video = peak ? Video() with { Bandwidth = long.MaxValue } : Video() with { AverageBandwidth = long.MaxValue };
            Assert.Throws<OverflowException>(() => HlsMasterPlaylist.Create([video], [Audio()]));
        }

        private static HlsVariant Video() => new("video.m3u8", "avc1.42C029", 400, 300, AudioGroupId: "audio");
        private static HlsAudioRendition Audio() => new("audio.m3u8", "audio", "English", "mp4a.40.2", 100, 80, 2);
    }
}
