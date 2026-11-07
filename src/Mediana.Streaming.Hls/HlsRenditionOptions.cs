// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Processing;

namespace Mediana.Streaming.Hls
{
    public record HlsRenditionOptions
    {
        public IReadOnlyList<HlsVideoProfile> VideoProfiles { get; init; } = [];
        public MediaDuration TargetDuration { get; init; } = new(2, new(1, 1));
        public bool SeparateAudio { get; init; }
        /// <summary>For separate audio, maps each video variant to an audio source profile. Defaults to profile zero.</summary>
        public IReadOnlyList<int>? AudioProfileIndices { get; init; }
        /// <summary>Shared orientation/precision/scaling stage applied before the individual rendition sizes.</summary>
        public VideoPreparationOptions FramePreparation { get; init; } = new();
    }
}
