// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    /// <summary>An AVC media-playlist reference with optional audio and measured complete-presentation bitrates in bits/second.
    /// Optional dimensions describe display pixels rather than stored samples. AudioGroupId associates an external audio group;
    /// in that case Codecs and bitrates describe the video-only playlist, and master creation adds the group's codecs and rates.</summary>
    public readonly record struct HlsVariant(string PlaylistUri, string Codecs, long Bandwidth, long AverageBandwidth,
        int? DisplayWidth = null, int? DisplayHeight = null, string? AudioGroupId = null);
}
