// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    /// <summary>TrackTimestampScale and native nanosecond timing declarations, independent of packet PTS and DTS.</summary>
    public readonly record struct MatroskaTrackTiming(double TimestampScale, ulong? DefaultDurationNanoseconds,
        ulong CodecDelayNanoseconds, ulong SeekPreRollNanoseconds);
}
