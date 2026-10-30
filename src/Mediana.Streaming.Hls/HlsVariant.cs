// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    /// <summary>A video media-playlist reference with measured complete-presentation bitrates in bits/second.
    /// Optional dimensions describe display pixels rather than stored samples.</summary>
    public readonly record struct HlsVariant(string PlaylistUri, string Codecs, long Bandwidth, long AverageBandwidth,
        int? DisplayWidth = null, int? DisplayHeight = null);
}
