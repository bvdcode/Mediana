// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal static class HlsPackagePlaylists
    {
        internal static async ValueTask WriteAsync(IHlsPackageOutput output, IReadOnlyList<HlsVideoRendition> video,
            IReadOnlyList<HlsAudioOutput> audio, IReadOnlyList<int> audioProfiles, CancellationToken token)
        {
            long? target = null;
            if (audio.Count > 0)
            {
                target = video.SelectMany(rendition => rendition.Writer.Segments)
                    .Concat(audio.SelectMany(rendition => rendition.Writer.Segments))
                    .Max(segment => new MediaTimestamp(segment.Duration.Value, segment.Duration.TimeBase)
                        .Rescale(new(1, 1), TimestampRounding.Ceiling).Value);
            }
            List<HlsVariant> variants = [];
            for (int index = 0; index < video.Count; index++)
            {
                HlsVariant variant = await video[index].WritePlaylistAsync(target, token).ConfigureAwait(false);
                if (audio.Count > 0) { variant = variant with { AudioGroupId = audio[audioProfiles[index]].GroupId }; }
                variants.Add(variant);
            }
            List<HlsAudioRendition> groups = [];
            foreach (HlsAudioOutput rendition in audio) { groups.Add(await rendition.WritePlaylistAsync(target!.Value, token).ConfigureAwait(false)); }
            await output.WritePlaylistAsync("index.m3u8", HlsMasterPlaylist.Create(variants, groups), token).ConfigureAwait(false);
        }

        internal static void ValidateAlignment(IReadOnlyList<HlsVideoRendition> renditions)
        {
            IReadOnlyList<HlsMediaSegment> first = renditions[0].Writer.Segments;
            foreach (HlsVideoRendition rendition in renditions.Skip(1))
            {
                IReadOnlyList<HlsMediaSegment> current = rendition.Writer.Segments;
                if (first.Count != current.Count || first.Where((segment, index) =>
                    segment.Start != current[index].Start || segment.Duration != current[index].Duration).Any())
                { throw new InvalidOperationException("HLS variant segment timelines differ."); }
            }
        }
    }
}
