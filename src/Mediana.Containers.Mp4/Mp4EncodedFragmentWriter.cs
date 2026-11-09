// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    /// <summary>Writes audiovisual ISO BMFF initialization and media fragments from encoded packets.
    /// Borrows declarations, packets, allocator and destination. Validates the complete header before publishing bytes.</summary>
    public static class Mp4EncodedFragmentWriter
    {
        public static async ValueTask WriteInitializationAsync(IReadOnlyList<Mp4TrackDefinition> tracks, Stream destination,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ValidateDestination(destination, allocator);
            ValidateTracks(tracks);
            using MediaBuffer header = Mp4EncodedFragmentInitialization.Create(tracks, allocator, cancellationToken);
            await destination.WriteAsync(header.Memory, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask<long> WriteFragmentAsync(IReadOnlyList<Mp4TrackPackets> tracks, uint sequenceNumber,
            Stream destination, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ValidateDestination(destination, allocator);
            ArgumentNullException.ThrowIfNull(tracks);
            ValidateTracks(tracks.Select(item => item?.Track ?? throw new ArgumentException("Fragment tracks cannot be null.", nameof(tracks))).ToArray());
            Mp4TrackPackets[] selected = tracks.Where(item => item.Packets.Count > 0).ToArray();
            if (selected.Length == 0) { throw new ArgumentException("A media fragment requires at least one packet.", nameof(tracks)); }
            Mp4FragmentTrack[] samples = selected.Select(item => (Mp4FragmentTrack)new Mp4PacketFragmentTrack(item.Track, item.Packets, cancellationToken)).ToArray();
            using MediaBuffer header = Mp4FragmentHeader.Create(samples, sequenceNumber, allocator, cancellationToken);
            await destination.WriteAsync(header.Memory, cancellationToken).ConfigureAwait(false);
            long bytes = header.Memory.Length;
            foreach (Mp4TrackPackets item in selected)
            {
                foreach (MediaPacket packet in item.Packets)
                {
                    await destination.WriteAsync(packet.Data, cancellationToken).ConfigureAwait(false);
                    bytes = checked(bytes + packet.Data.Length);
                }
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return bytes;
        }

        internal static void ValidateTracks(IReadOnlyList<Mp4TrackDefinition> tracks)
        {
            ArgumentNullException.ThrowIfNull(tracks);
            if (tracks.Count == 0 || tracks.Any(track => track is null) ||
                tracks.Select(track => track.TrackId).Distinct().Count() != tracks.Count ||
                tracks.Select(track => track.Stream.StreamIndex).Distinct().Count() != tracks.Count)
            { throw new ArgumentException("Supply tracks with unique MP4 and stream identities.", nameof(tracks)); }
        }

        private static void ValidateDestination(Stream destination, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            if (!destination.CanWrite) { throw new ArgumentException("The fragment destination must be writable.", nameof(destination)); }
        }
    }
}
