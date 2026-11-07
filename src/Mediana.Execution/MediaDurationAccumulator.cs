// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Sums known input durations exactly, deriving a common clock when needed.
    /// A missing duration makes the total unknown for the remainder of the operation. Overflow throws.</summary>
    public class MediaDurationAccumulator
    {
        private MediaDuration? duration;
        private bool unknown;

        public MediaDuration? Duration => unknown ? null : duration;

        public void Add(MediaDuration? next)
        {
            if (next is not MediaDuration span)
            {
                unknown = true;
                return;
            }
            span.TimeBase.Validate();
            if (unknown)
            {
                return;
            }
            if (duration is not MediaDuration previous)
            {
                duration = span;
            }
            else if (previous.TimeBase == span.TimeBase)
            {
                duration = new(checked(previous.Value + span.Value), previous.TimeBase);
            }
            else
            {
                MediaTimestamp left = new MediaTimestamp(previous.Value, previous.TimeBase).ReduceClock();
                MediaTimestamp right = new MediaTimestamp(span.Value, span.TimeBase).ReduceClock();
                TimeBase clock = TimeBase.Common(left.TimeBase, right.TimeBase);
                long ticks = checked(left.Rescale(clock, TimestampRounding.TowardZero).Value +
                    right.Rescale(clock, TimestampRounding.TowardZero).Value);
                duration = new(ticks, clock);
            }
        }
    }
}
