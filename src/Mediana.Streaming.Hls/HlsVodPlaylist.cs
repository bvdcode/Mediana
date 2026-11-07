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
            Func<int, string> segmentUri, int maximumUriCharacters, bool independent = false, long? targetDurationSeconds = null,
            string? initializationUri = null)
        {
            ArgumentNullException.ThrowIfNull(segmentUri);
            long target = GetTargetDuration(segments, durationOf, targetDurationSeconds);
            StringBuilder text = new();
            text.Append(initializationUri is null ? "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:" :
                "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:");
            text.Append(target.ToString(CultureInfo.InvariantCulture));
            text.Append("\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n");
            if (initializationUri is not null)
            {
                HlsUri.Validate(initializationUri, maximumUriCharacters);
                if (initializationUri.Contains('"')) { throw new ArgumentException("An initialization URI cannot contain a quote.", nameof(initializationUri)); }
                text.Append("#EXT-X-MAP:URI=\"");
                text.Append(initializationUri);
                text.Append("\"\n");
            }
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

        public static long GetTargetDuration<T>(IReadOnlyList<T> segments, Func<T, MediaDuration> durationOf, long? requested)
        {
            if (requested is not long target)
            {
                return segments.Max(segment => new MediaTimestamp(durationOf(segment).Value, durationOf(segment).TimeBase)
                    .Rescale(new(1, 1), TimestampRounding.Ceiling).Value);
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target);
            foreach (T segment in segments)
            {
                MediaDuration duration = durationOf(segment);
                Int128 numerator = (Int128)duration.Value * duration.TimeBase.Numerator * 2 + duration.TimeBase.Denominator;
                Int128 rounded = numerator / (2L * duration.TimeBase.Denominator);
                if (rounded > target)
                {
                    throw new ArgumentException("A segment's rounded duration exceeds the configured playlist TARGETDURATION.", nameof(requested));
                }
            }
            return target;
        }
    }
}
