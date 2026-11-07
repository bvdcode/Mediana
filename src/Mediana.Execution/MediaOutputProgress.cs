// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Output bytes include encoding and container overhead. Total duration is optional and comes from the input declaration.</summary>
    public record MediaOutputProgress(MediaDuration? ProcessedDuration, long BytesWritten, bool IsComplete,
        MediaDuration? TotalDuration = null);
}
