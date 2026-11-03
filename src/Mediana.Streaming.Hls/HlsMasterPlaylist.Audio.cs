// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;

namespace Mediana.Streaming.Hls
{
    public static partial class HlsMasterPlaylist
    {
        private static Dictionary<string, List<HlsAudioRendition>> ValidateAudio(IReadOnlyList<HlsAudioRendition> audio, int maximumUriCharacters)
        {
            Dictionary<string, List<HlsAudioRendition>> groups = new(StringComparer.Ordinal);
            foreach (HlsAudioRendition rendition in audio)
            {
                HlsUri.Validate(rendition.PlaylistUri, maximumUriCharacters);
                HlsAttribute.Validate(rendition.PlaylistUri);
                HlsAttribute.Validate(rendition.GroupId);
                HlsAttribute.Validate(rendition.Name);
                if (rendition.Language is string language)
                {
                    HlsAttribute.Validate(language);
                }
                if (rendition.Codecs is not ("mp4a.40.2" or "mp4a.6B" or "mp4a.69") || rendition.Bandwidth <= 0 ||
                    rendition.AverageBandwidth <= 0 || rendition.Channels <= 0 || (rendition.IsDefault && !rendition.AutoSelect))
                {
                    throw new ArgumentException("Audio renditions require a supported codec, positive transport rates and channel count; DEFAULT=YES requires AUTOSELECT=YES.", nameof(audio));
                }
                if (!groups.TryGetValue(rendition.GroupId, out List<HlsAudioRendition>? members))
                {
                    members = [];
                    groups.Add(rendition.GroupId, members);
                }
                if (members.Any(member => member.Name == rendition.Name || (member.IsDefault && rendition.IsDefault)))
                {
                    throw new ArgumentException("Audio group names must be unique and at most one member may be the default.", nameof(audio));
                }
                members.Add(rendition);
            }
            List<HlsAudioRendition>? reference = null;
            foreach (List<HlsAudioRendition> members in groups.Values)
            {
                reference ??= members;
                if (members.Count != reference.Count || members.Any(member => !reference.Any(other =>
                    other.Name == member.Name && other.Language == member.Language &&
                    other.IsDefault == member.IsDefault && other.AutoSelect == member.AutoSelect)))
                {
                    throw new ArgumentException("Audio groups must contain corresponding members with matching selection attributes.", nameof(audio));
                }
            }
            return groups;
        }

        private static HlsVariant ResolveAudio(HlsVariant variant, Dictionary<string, List<HlsAudioRendition>> groups)
        {
            if (variant.AudioGroupId is not string groupId)
            {
                return variant;
            }
            if (variant.Codecs.Length != 11 || !groups.TryGetValue(groupId, out List<HlsAudioRendition>? members))
            {
                throw new ArgumentException("An external audio group requires a video-only variant and a matching declared group.", nameof(variant));
            }
            string codecs = variant.Codecs + "," + string.Join(',', members.Select(member => member.Codecs).Distinct(StringComparer.Ordinal));
            return variant with
            {
                Codecs = codecs,
                Bandwidth = checked(variant.Bandwidth + members.Max(member => member.Bandwidth)),
                AverageBandwidth = checked(variant.AverageBandwidth + members.Max(member => member.AverageBandwidth))
            };
        }

        private static void AppendAudio(StringBuilder text, IReadOnlyList<HlsAudioRendition> audio)
        {
            foreach (HlsAudioRendition rendition in audio)
            {
                text.Append("#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"");
                text.Append(rendition.GroupId);
                text.Append("\",NAME=\"");
                text.Append(rendition.Name);
                text.Append("\",URI=\"");
                text.Append(rendition.PlaylistUri);
                text.Append("\",CHANNELS=\"");
                text.Append(rendition.Channels.ToString(CultureInfo.InvariantCulture));
                text.Append('"');
                if (rendition.Language is string language)
                {
                    text.Append(",LANGUAGE=\"");
                    text.Append(language);
                    text.Append('"');
                }
                text.Append(rendition.IsDefault ? ",DEFAULT=YES" : ",DEFAULT=NO");
                text.Append(rendition.AutoSelect ? ",AUTOSELECT=YES\n" : ",AUTOSELECT=NO\n");
            }
        }
    }
}
