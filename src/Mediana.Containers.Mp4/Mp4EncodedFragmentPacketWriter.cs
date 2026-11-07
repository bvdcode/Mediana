// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    /// <summary>Retains only one fragment's compressed packets and writes its header and payload at completion.
    /// Packet leases, destination and allocator remain borrowed. A failed fragment must be discarded.</summary>
    public class Mp4EncodedFragmentPacketWriter : IContainerWriter
    {
        private readonly Mp4TrackDefinition[] tracks;
        private readonly Dictionary<int, List<MediaPacket>> packets;
        private readonly uint sequence;
        private readonly Stream destination;
        private readonly MediaBufferAllocator allocator;
        private bool completed;
        private bool disposed;
        private int busy;

        public Mp4EncodedFragmentPacketWriter(IReadOnlyList<Mp4TrackDefinition> tracks, uint sequence,
            Stream destination, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(tracks);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            Mp4EncodedFragmentWriter.ValidateTracks(tracks);
            if (!destination.CanWrite) { throw new ArgumentException("The fragment destination must be writable.", nameof(destination)); }
            this.tracks = tracks.ToArray();
            packets = tracks.ToDictionary(track => track.Stream.StreamIndex, _ => new List<MediaPacket>());
            this.sequence = sequence;
            this.destination = destination;
            this.allocator = allocator;
        }

        public long BytesWritten { get; private set; }

        public ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                EnsureWritable();
                ArgumentNullException.ThrowIfNull(packet);
                cancellationToken.ThrowIfCancellationRequested();
                if (!packets.TryGetValue(packet.StreamIndex, out List<MediaPacket>? selected))
                { throw new InvalidDataException("The packet does not belong to a declared fragment track."); }
                MediaPacket lease = packet.Retain();
                bool retained = false;
                try { selected.Add(lease); retained = true; }
                finally { if (!retained) { lease.Dispose(); } }
                return ValueTask.CompletedTask;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                EnsureWritable();
                completed = true;
                BytesWritten = await Mp4EncodedFragmentWriter.WriteFragmentAsync(
                    tracks.Select(track => new Mp4TrackPackets(track, packets[track.Stream.StreamIndex])).ToArray(),
                    sequence, destination, allocator, cancellationToken).ConfigureAwait(false);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    foreach (List<MediaPacket> selected in packets.Values)
                    { foreach (MediaPacket packet in selected) { packet.Dispose(); } selected.Clear(); }
                }
                return ValueTask.CompletedTask;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void EnsureWritable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed) { throw new InvalidOperationException("The fragment has already been completed."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Fragment writer operations must not overlap."); }
        }
    }
}
