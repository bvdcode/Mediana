// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    /// <summary>Native Info values. Duration uses Segment Ticks; TimestampScale is nanoseconds per Segment Tick.
    /// DateNanoseconds is relative to 2001-01-01 UTC.</summary>
    public record MatroskaSegmentInfo(ulong TimestampScale, double? Duration,
        long? DateNanoseconds, string? Title, string MuxingApp, string WritingApp,
        Guid? Uid, Guid? PreviousSegment, Guid? NextSegment);
}
