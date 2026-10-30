// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;

namespace Mediana.Streaming.Hls
{
    /// <summary>Formats AVC VOD variants with optional AAC-LC or MPEG-1/2 Layer III audio.
    /// The caller must provide matching timelines and measured metadata.</summary>
    public static class HlsMasterPlaylist
    {
        public static string Create(IReadOnlyList<HlsVariant> variants, int maximumUriCharacters = 4096)
        {
            ArgumentNullException.ThrowIfNull(variants);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUriCharacters);
            if (variants.Count == 0)
            {
                throw new ArgumentException("An HLS master playlist requires at least one variant.", nameof(variants));
            }
            HashSet<string> uris = new(StringComparer.Ordinal);
            StringBuilder text = new("#EXTM3U\n#EXT-X-VERSION:3\n");
            foreach (HlsVariant variant in variants)
            {
                HlsUri.Validate(variant.PlaylistUri, maximumUriCharacters);
                if (!uris.Add(variant.PlaylistUri) || variant.Bandwidth <= 0 || variant.AverageBandwidth <= 0 ||
                    !ValidCodecs(variant.Codecs) ||
                    (variant.DisplayWidth is null) != (variant.DisplayHeight is null) || variant.DisplayWidth is <= 0 || variant.DisplayHeight is <= 0)
                {
                    throw new ArgumentException("HLS variants require unique URIs, positive bitrates, an AVC codec identifier and optional positive display dimensions.", nameof(variants));
                }
                text.Append("#EXT-X-STREAM-INF:BANDWIDTH=");
                text.Append(variant.Bandwidth.ToString(CultureInfo.InvariantCulture));
                text.Append(",AVERAGE-BANDWIDTH=");
                text.Append(variant.AverageBandwidth.ToString(CultureInfo.InvariantCulture));
                text.Append(",CODECS=\"");
                text.Append(variant.Codecs);
                text.Append('"');
                if (variant.DisplayWidth is int width && variant.DisplayHeight is int height)
                {
                    text.Append(",RESOLUTION=");
                    text.Append(width.ToString(CultureInfo.InvariantCulture));
                    text.Append('x');
                    text.Append(height.ToString(CultureInfo.InvariantCulture));
                }
                text.Append('\n');
                text.Append(variant.PlaylistUri);
                text.Append('\n');
            }
            return text.ToString();
        }

        private static bool ValidCodecs(string? codecs)
        {
            if (codecs is null || codecs.Length < 11 || !codecs.StartsWith("avc1.", StringComparison.Ordinal) ||
                codecs.AsSpan(5, 6).ContainsAnyExcept("0123456789abcdefABCDEF"))
            {
                return false;
            }
            return codecs[11..] is "" or ",mp4a.40.2" or ",mp4a.6B" or ",mp4a.69";
        }
    }
}
