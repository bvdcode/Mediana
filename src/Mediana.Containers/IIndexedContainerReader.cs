// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>Independent packet access that preserves the reader's sequential cursors.</summary>
    public interface IIndexedContainerReader : IContainerReader
    {
        long GetPacketCount(int streamIndex);

        /// <summary>Finds the last declared sync packet in decode order before the exclusive index
        /// whose PTS is at or before target. Packet zero is also a candidate, even when target precedes
        /// its PTS. Returns null when the search range is empty. Does not read encoded media.</summary>
        ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default);

        ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default);
    }
}
