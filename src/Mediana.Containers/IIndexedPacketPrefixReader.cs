// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers
{
    /// <summary>Reads bounded prefixes of logical encoded packets without moving sequential cursors.
    /// A prefix does not establish payload validity or decoder support.</summary>
    public interface IIndexedPacketPrefixReader : IIndexedContainerReader
    {
        /// <summary>Copies the smaller of destination length and packet length, returning the copied byte count.
        /// Container framing is excluded and any supported packet reconstruction is applied before slicing.</summary>
        ValueTask<int> ReadPacketPrefixAtAsync(int streamIndex, long packetIndex, Memory<byte> destination,
            CancellationToken cancellationToken = default);
    }
}
