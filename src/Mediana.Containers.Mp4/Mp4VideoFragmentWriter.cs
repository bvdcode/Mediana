// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    /// <summary>Writes initialization and one independently requested video fragment from borrowed encoded packets.
    /// Packet leases and destination belong to the caller. Failed output must be discarded.</summary>
    public static class Mp4VideoFragmentWriter
    {
        public static async ValueTask WriteInitializationAsync(Mp4VideoTrackDefinition track, Stream destination,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            Validate(track, destination, allocator);
            await Mp4EncodedFragmentWriter.WriteInitializationAsync([track], destination, allocator, cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask<long> WriteFragmentAsync(Mp4VideoTrackDefinition track, IReadOnlyList<MediaPacket> packets,
            uint sequenceNumber, Stream destination, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            Validate(track, destination, allocator);
            ArgumentNullException.ThrowIfNull(packets);
            return await Mp4EncodedFragmentWriter.WriteFragmentAsync([new(track, packets)], sequenceNumber,
                destination, allocator, cancellationToken).ConfigureAwait(false);
        }

        private static void Validate(Mp4VideoTrackDefinition track, Stream destination, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(track);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            if (!destination.CanWrite) { throw new ArgumentException("The fragment destination must be writable.", nameof(destination)); }
        }
    }
}
