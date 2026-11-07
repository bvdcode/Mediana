// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Timing
{
    /// <summary>An exact half-open presentation interval on a common clock. Negative timestamps are allowed.
    /// Does not imply codec restart safety, frame alignment or a change of timeline origin.</summary>
    public readonly record struct MediaTimeRange
    {
        public MediaTimeRange(MediaTimestamp start, MediaTimestamp end)
        {
            TimeBase clock = TimeBase.Common(start.TimeBase, end.TimeBase);
            if (end.CompareTo(start) < 0) { throw new ArgumentOutOfRangeException(nameof(end), "The interval end must not precede its start."); }
            Start = start.Rescale(clock, TimestampRounding.TowardZero);
            End = end.Rescale(clock, TimestampRounding.TowardZero);
            _ = checked(End.Value - Start.Value);
        }

        public MediaTimestamp Start { get; }
        public MediaTimestamp End { get; }
        public MediaDuration Duration => new(checked(End.Value - Start.Value), Start.TimeBase);
    }
}
