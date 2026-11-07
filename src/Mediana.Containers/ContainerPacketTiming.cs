// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>Indexed packet clocks without encoded payload. Duration retains the container's packet interval;
    /// it need not equal the displayed interval of a reordered video picture. Null values remain unknown.</summary>
    public readonly record struct ContainerPacketTiming(MediaTimestamp? PresentationTime, MediaTimestamp? DecodeTime,
        MediaDuration? Duration, bool? IsKeyFrame);
}
