// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    public record HlsPlanningOptions
    {
        /// <summary>Choose the first eligible IDR at or after this interval from each segment's presentation start.</summary>
        public MediaDuration TargetDuration { get; init; } = new(6, new TimeBase(1, 1));
        public int MaximumSegments { get; init; } = 100_000;

        internal void Validate()
        {
            TargetDuration.TimeBase.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TargetDuration.Value);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSegments);
        }
    }
}
