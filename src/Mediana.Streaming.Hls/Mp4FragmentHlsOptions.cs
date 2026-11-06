// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;

namespace Mediana.Streaming.Hls
{
    public record Mp4FragmentHlsOptions
    {
        public Mp4ReadOptions Reading { get; init; } = new() { TimestampMode = Mp4TimestampMode.LinearMovie };
        public HlsPlanningOptions Planning { get; init; } = new();
        public int MaximumSegmentUriCharacters { get; init; } = 4096;

        internal void Validate()
        {
            ArgumentNullException.ThrowIfNull(Reading);
            ArgumentNullException.ThrowIfNull(Planning);
            Planning.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSegmentUriCharacters);
            if (Reading.TimestampMode != Mp4TimestampMode.LinearMovie)
            {
                throw new ArgumentException("Fragment HLS planning requires the linear movie timeline.", nameof(Reading));
            }
        }
    }
}
