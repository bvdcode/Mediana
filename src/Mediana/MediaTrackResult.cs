// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana
{
    /// <summary>Track counters after muxer completion. Encoder delay/padding are samples per channel;
    /// null means unavailable or copied. They are not silently removed from compressed output.</summary>
    public record MediaTrackResult(int InputStreamIndex, EncodedStreamInfo Stream, long Frames,
        long SamplesPerChannel, long Packets, long PayloadBytes, int? PrimingSamples, int? PaddingSamples);
}
