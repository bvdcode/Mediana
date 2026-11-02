// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers
{
    /// <summary>Indexed restart positioning for codecs that reconstruct state from preceding encoded packets.</summary>
    public interface IIndexedPrerollReader : IIndexedContainerReader
    {
        /// <summary>Combines codec history with container recovery declarations for an output packet.
        /// Returns the decode start, at or before that packet, without reading encoded media or moving cursors.
        /// History is clamped at the original stream beginning. Unsupported recovery requirements must fail explicitly.</summary>
        ContainerSeekPoint GetPrerollStart(int streamIndex, long outputPacketIndex, int minimumPrecedingPackets,
            CancellationToken cancellationToken = default);
    }
}
