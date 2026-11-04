// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;

namespace Mediana.Streaming.Hls
{
    /// <summary>Formats AVC VOD variants with optional AAC-LC or MPEG-1/2 Layer III audio.
    /// External audio groups are combined with video-only playlists. The caller must provide matching timelines,
    /// common target durations and measured metadata; this formatter does not read media playlists.</summary>
    public static partial class HlsMasterPlaylist
    {
        public static string Create(IReadOnlyList<HlsVariant> variants, int maximumUriCharacters = 4096) =>
            Create(variants, [], maximumUriCharacters);

        public static string Create(IReadOnlyList<HlsVariant> variants, IReadOnlyList<HlsAudioRendition> audio,
            int maximumUriCharacters = 4096)
        {
            ArgumentNullException.ThrowIfNull(variants);
            ArgumentNullException.ThrowIfNull(audio);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUriCharacters);
            if (variants.Count == 0)
            {
                throw new ArgumentException("An HLS master playlist requires at least one variant.", nameof(variants));
            }
            Dictionary<string, List<HlsAudioRendition>> groups = ValidateAudio(audio, maximumUriCharacters);
            HashSet<(string Uri, string? AudioGroup)> references = [];
            StringBuilder text = new("#EXTM3U\n#EXT-X-VERSION:3\n");
            AppendAudio(text, audio);
            foreach (HlsVariant variant in variants)
            {
                HlsUri.Validate(variant.PlaylistUri, maximumUriCharacters);
                if (!references.Add((variant.PlaylistUri, variant.AudioGroupId)) || variant.Bandwidth <= 0 || variant.AverageBandwidth <= 0 ||
                    !ValidCodecs(variant.Codecs) ||
                    (variant.DisplayWidth is null) != (variant.DisplayHeight is null) || variant.DisplayWidth is <= 0 || variant.DisplayHeight is <= 0)
                {
                    throw new ArgumentException("HLS variants require unique playlist/audio-group pairs, positive bitrates, an AVC codec identifier and optional positive display dimensions.", nameof(variants));
                }
                HlsVariant resolved = ResolveAudio(variant, groups);
                text.Append("#EXT-X-STREAM-INF:BANDWIDTH=");
                text.Append(resolved.Bandwidth.ToString(CultureInfo.InvariantCulture));
                text.Append(",AVERAGE-BANDWIDTH=");
                text.Append(resolved.AverageBandwidth.ToString(CultureInfo.InvariantCulture));
                text.Append(",CODECS=\"");
                text.Append(resolved.Codecs);
                text.Append('"');
                if (variant.AudioGroupId is string groupId)
                {
                    text.Append(",AUDIO=\"");
                    text.Append(groupId);
                    text.Append('"');
                }
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
