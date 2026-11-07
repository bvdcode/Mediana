// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal record ProcessingPacket(int Stream, MediaTimestamp? Pts, MediaTimestamp? Dts, MediaDuration? Duration, byte[] Data);
}
