// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Codecs
{
    internal readonly record struct StartCodePacketTiming(long Offset, MediaTimestamp? Pts, MediaTimestamp? Dts,
        MediaDuration? Duration, bool? KeyFrame);
}
