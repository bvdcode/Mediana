// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana
{
    internal class MediaInputProgress
    {
        private readonly MediaDurationAccumulator processed = new();
        public long Frames { get; private set; }
        public MediaDuration? Duration => processed.Duration;

        public void Add(MediaDuration? duration)
        {
            processed.Add(duration);
            Frames = checked(Frames + 1);
        }
    }
}
