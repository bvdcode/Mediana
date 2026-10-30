// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>A container candidate for restarting decoding. Packet indices are zero-based in decode order.
    /// A candidate requires independent codec validation before use.</summary>
    public readonly record struct ContainerSeekPoint(int StreamIndex, long PacketIndex, MediaTimestamp PresentationTime);
}
