// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>One completed video segment. Byte length includes the complete transport overhead.</summary>
    public readonly record struct HlsMediaSegment(int Number, MediaTimestamp Start, MediaDuration Duration, long ByteLength);
}
