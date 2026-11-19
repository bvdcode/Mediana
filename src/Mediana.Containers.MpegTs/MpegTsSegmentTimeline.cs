// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.MpegTs
{
    /// <summary>Final decode timestamps of the preceding indexed segment, before the writer's timestamp offset.
    /// The clock track is AVC when present, otherwise the first configured audio track.</summary>
    public record MpegTsSegmentTimeline(MediaTimestamp LastDecodeTime, MediaTimestamp LastClockDecodeTime);
}
