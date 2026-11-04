// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsAudioGroupTestInput
    {
        public static void Verify()
        {
            HlsAudioRendition english = new("audio/en.m3u8", "audio", "English", "mp4a.40.2", 50, 49, 2)
            {
                Language = "en",
                IsDefault = true,
                AutoSelect = true
            };
            HlsAudioRendition spanish = new("audio/es.m3u8", "audio", "Español, estéreo", "mp4a.6B", 100, 40, 2)
            {
                Language = "es",
                AutoSelect = true
            };
            HlsAudioRendition french = new("audio/fr.m3u8", "audio", "Français", "mp4a.69", 80, 30, 1)
            {
                Language = "fr",
                AutoSelect = true
            };
            string master = HlsMasterPlaylist.Create([
                new("video.m3u8", "avc1.42C029", 400, 300, 640, 360, "audio")], [english, spanish, french]);
            string expected = "#EXTM3U\n#EXT-X-VERSION:3\n" +
                "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"English\",URI=\"audio/en.m3u8\",CHANNELS=\"2\",LANGUAGE=\"en\",DEFAULT=YES,AUTOSELECT=YES\n" +
                "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"Español, estéreo\",URI=\"audio/es.m3u8\",CHANNELS=\"2\",LANGUAGE=\"es\",DEFAULT=NO,AUTOSELECT=YES\n" +
                "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"Français\",URI=\"audio/fr.m3u8\",CHANNELS=\"1\",LANGUAGE=\"fr\",DEFAULT=NO,AUTOSELECT=YES\n" +
                "#EXT-X-STREAM-INF:BANDWIDTH=500,AVERAGE-BANDWIDTH=349,CODECS=\"avc1.42C029,mp4a.40.2,mp4a.6B,mp4a.69\",AUDIO=\"audio\",RESOLUTION=640x360\nvideo.m3u8\n";
            if (master != expected)
            {
                throw new InvalidDataException("The HLS audio group changed selection attributes, codec union or maximum aggregate transport rates.");
            }
        }
    }
}
