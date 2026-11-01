// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaPacketTiming(MediaTimestamp? PresentationTime,
        MediaDuration? Duration, bool IsKeyFrame);
}
