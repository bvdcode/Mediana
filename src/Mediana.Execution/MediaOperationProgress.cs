// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Completed input frames and output packets. PayloadBytes excludes container overhead.
    /// ProcessedDuration sums known input frame durations; null means the duration cannot be determined.</summary>
    public record MediaOperationProgress(long Frames, long Packets, long PayloadBytes, MediaDuration? ProcessedDuration, bool IsComplete);
}
