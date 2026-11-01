// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal record MatroskaSegmentInfo(ulong TimestampScale, double? Duration,
        long? DateNanoseconds, string? Title, string MuxingApp, string WritingApp,
        Guid? Uid, Guid? PreviousSegment, Guid? NextSegment);
}
