// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Execution
{
    /// <summary>A contiguous compressed packet range in decode order.</summary>
    public readonly record struct IndexedPacketRange(long FirstPacket, long PacketCount);
}
