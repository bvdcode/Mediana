// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>A displayed picture associated with a coded packet. The ordinal identifies its decoder
    /// output within that packet. Unknown presentation timestamps and extents remain null.</summary>
    public readonly record struct ContainerVideoPresentationUnit(long PacketIndex, int OutputOrdinal,
        MediaTimestamp? PresentationTime, MediaDuration? Duration);
}
