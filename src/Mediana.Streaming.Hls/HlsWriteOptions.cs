// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    public record HlsWriteOptions
    {
        /// <summary>Interval between nominal boundaries; writers select complete access units or eligible key frames.</summary>
        public MediaDuration TargetDuration { get; init; } = new(6, new(1, 1));
        /// <summary>Optional common EXT-X-TARGETDURATION for associated playlists, in seconds.
        /// Each completed segment's duration rounded to the nearest integer must not exceed it. Null selects the measured ceiling.</summary>
        public long? PlaylistTargetDurationSeconds { get; init; }
        public MpegTsWriteOptions Transport { get; init; } = new();
        public int? MaximumSegments { get; init; }
        public int MaximumSegmentUriCharacters { get; init; } = 4096;

        internal void Validate()
        {
            TargetDuration.TimeBase.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TargetDuration.Value);
            ArgumentNullException.ThrowIfNull(Transport);
            if (PlaylistTargetDurationSeconds is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(PlaylistTargetDurationSeconds));
            }
            if (MaximumSegments is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumSegments));
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSegmentUriCharacters);
        }
    }
}
