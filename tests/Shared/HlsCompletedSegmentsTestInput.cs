// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsCompletedSegmentsTestInput
    {
        public static void Verify()
        {
            TimeBase clock = new(1, 48000);
            HlsMediaSegment[] segments =
            [
                new(0, new(-1024, clock), new(288000, clock), 3000),
                new(1, new(286976, clock), new(288000, clock), 6000),
                new(2, new(574976, clock), new(144000, clock), 1500)
            ];
            HlsMediaSegment[] original = [.. segments];
            string playlist = HlsMediaPlaylist.Create(segments, number => $"segment-{number}.ts", targetDurationSeconds: 6);
            if (playlist != "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:6\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n#EXTINF:6,\nsegment-0.ts\n#EXTINF:6,\nsegment-1.ts\n#EXTINF:3,\nsegment-2.ts\n#EXT-X-ENDLIST\n" ||
                HlsMediaPlaylist.MeasureBandwidth(segments, 6) != (8000, 5600) || !segments.SequenceEqual(original))
            {
                throw new InvalidOperationException("Completed HLS metadata changed its exact playlist, reference bitrates or caller storage.");
            }
            segments[1] = segments[1] with { ByteLength = 9000 };
            if (HlsMediaPlaylist.MeasureBandwidth(segments, 6) != (12000, 7200) ||
                HlsMediaPlaylist.Create(segments, number => $"{number}.ts", true, 6).Contains("#EXT-X-INDEPENDENT-SEGMENTS\n", StringComparison.Ordinal) is false)
            {
                throw new InvalidOperationException("Completed HLS measurements did not use actual replacement sizes or explicit independence.");
            }
            TimeBase thirds = new(1, 3);
            HlsMediaSegment[] uneven =
            [
                new(0, new(3, thirds), new(5, thirds), 1001),
                new(1, new(8, thirds), new(1, thirds), 1000)
            ];
            if (HlsMediaPlaylist.MeasureBandwidth(uneven) != (8004, 8004))
            {
                throw new InvalidOperationException("Completed HLS rates did not include the eligible unequal-duration segment set.");
            }
        }
    }
}
