using System.Globalization;
using System.Text;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal static class HlsVodPlaylist
    {
        public static string Create(IReadOnlyList<Mp4HlsSegment> segments, Func<int, string> segmentUri, int maximumUriCharacters)
        {
            ArgumentNullException.ThrowIfNull(segmentUri);
            long target = segments.Max(segment => new MediaTimestamp(segment.Duration.Value, segment.Duration.TimeBase)
                .Rescale(new(1, 1), TimestampRounding.Ceiling).Value);
            StringBuilder text = new();
            text.Append("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:");
            text.Append(target.ToString(CultureInfo.InvariantCulture));
            text.Append("\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n");
            foreach (Mp4HlsSegment segment in segments)
            {
                string uri = segmentUri(segment.Number);
                if (string.IsNullOrEmpty(uri) || uri.Length > maximumUriCharacters || uri[0] == '#' ||
                    uri.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) ||
                    !Uri.IsWellFormedUriString(uri, UriKind.RelativeOrAbsolute))
                {
                    throw new ArgumentException("Segment URIs must be well-formed, bounded URI lines without whitespace or control characters.", nameof(segmentUri));
                }
                decimal duration = (decimal)segment.Duration.Value * segment.Duration.TimeBase.Numerator / segment.Duration.TimeBase.Denominator;
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
