// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    /// <summary>An external audio media playlist with measured transport bitrates and codec channel count.
    /// Group members represent alternative versions of the same content. Language, when supplied, is a BCP 47 tag.</summary>
    public readonly record struct HlsAudioRendition(string PlaylistUri, string GroupId, string Name, string Codecs,
        long Bandwidth, long AverageBandwidth, int Channels)
    {
        public string? Language { get; init; }
        public bool IsDefault { get; init; }
        public bool AutoSelect { get; init; }
    }
}
