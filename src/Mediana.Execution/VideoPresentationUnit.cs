// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Execution
{
    internal readonly record struct VideoPresentationUnit(MediaTimestamp Start, MediaDuration? Duration, long PacketIndex);
}
