// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Sums input durations in one time base. A missing duration or a change of time base
    /// makes the total unknown for the remainder of the operation. Overflow throws.</summary>
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
            else if (previous.TimeBase != span.TimeBase)
            {
                unknown = true;
            }
            else
            {
                duration = new(checked(previous.Value + span.Value), previous.TimeBase);
            }
        }
    }
}
