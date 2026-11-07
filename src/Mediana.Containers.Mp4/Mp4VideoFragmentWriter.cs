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
            using MediaBuffer header = Mp4VideoFragmentInitialization.Create(track, allocator, cancellationToken);
            await destination.WriteAsync(header.Memory, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask<long> WriteFragmentAsync(Mp4VideoTrackDefinition track, IReadOnlyList<MediaPacket> packets,
            uint sequenceNumber, Stream destination, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            Validate(track, destination, allocator);
            ArgumentNullException.ThrowIfNull(packets);
            Mp4PacketFragmentTrack samples = new(track, packets, cancellationToken);
            using MediaBuffer header = Mp4FragmentHeader.Create([samples], sequenceNumber, allocator, cancellationToken);
            await destination.WriteAsync(header.Memory, cancellationToken).ConfigureAwait(false);
            long bytes = header.Memory.Length;
            foreach (MediaPacket packet in packets)
            {
                await destination.WriteAsync(packet.Data, cancellationToken).ConfigureAwait(false);
                bytes = checked(bytes + packet.Data.Length);
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return bytes;
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
