// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal static class HlsVodPlaylist
    {
        public static string Create<T>(IReadOnlyList<T> segments, Func<T, MediaDuration> durationOf,
            Func<int, string> segmentUri, int maximumUriCharacters, bool independent = false)
        {
            ArgumentNullException.ThrowIfNull(segmentUri);
            long target = segments.Max(segment => new MediaTimestamp(durationOf(segment).Value, durationOf(segment).TimeBase)
                .Rescale(new(1, 1), TimestampRounding.Ceiling).Value);
            StringBuilder text = new();
            text.Append("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:");
            text.Append(target.ToString(CultureInfo.InvariantCulture));
            text.Append("\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n");
            if (independent)
            {
                text.Append("#EXT-X-INDEPENDENT-SEGMENTS\n");
            }
            for (int number = 0; number < segments.Count; number++)
            {
                string uri = segmentUri(number);
                HlsUri.Validate(uri, maximumUriCharacters);
                MediaDuration interval = durationOf(segments[number]);
                decimal duration = (decimal)interval.Value * interval.TimeBase.Numerator / interval.TimeBase.Denominator;
                text.Append("#EXTINF:");
                text.Append(duration.ToString("0.###############", CultureInfo.InvariantCulture));
                text.Append(",\n");
                text.Append(uri);
                text.Append('\n');
            }
            text.Append("#EXT-X-ENDLIST\n");
            return text.ToString();
        }
    }
}
