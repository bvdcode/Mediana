// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    public record AvcHlsWriteOptions
    {
        /// <summary>Start a new segment at the first IDR at or after this interval from its start.</summary>
        public MediaDuration TargetDuration { get; init; } = new(6, new(1, 1));
        public MpegTsWriteOptions Transport { get; init; } = new();
        public int? MaximumSegments { get; init; }
        public int MaximumSegmentUriCharacters { get; init; } = 4096;

        internal void Validate()
        {
            TargetDuration.TimeBase.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TargetDuration.Value);
            ArgumentNullException.ThrowIfNull(Transport);
            if (MaximumSegments is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumSegments));
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSegmentUriCharacters);
        }
    }
}
