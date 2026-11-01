// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaTrackTiming(double TimestampScale, ulong? DefaultDurationNanoseconds,
        ulong CodecDelayNanoseconds, ulong SeekPreRollNanoseconds);
}
