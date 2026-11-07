// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers
{
    /// <summary>Optional indexed clock metadata. Queries preserve all packet cursors and read no encoded payload.
    /// Packet indices follow the same per-stream order as IIndexedContainerReader.</summary>
    public interface IIndexedPacketTimingReader : IIndexedContainerReader
    {
        ContainerPacketTiming GetPacketTiming(int streamIndex, long packetIndex, CancellationToken cancellationToken = default);
    }
}
