// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Ogg
{
    /// <summary>Indexes Theora/Opus/Vorbis packets in a complete Ogg source, including separate logical streams and links.
    /// Opening scans and verifies pages, retaining only headers and packet locations, not compressed media.
    /// Borrows an immutable random-access source. Operations must not overlap; failed reads can be retried.</summary>
    public class OggReader : IIndexedPrerollReader, IIndexedPresentationTimingReader, IIndexedPacketTimingReader
    {
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly OggReadOptions options;
        private readonly OggPageCache pages;
        private readonly List<OggTrack> tracks = [];
        private bool disposed;
        private int busy;

        private OggReader(IRandomAccessByteSource source, MediaBufferAllocator allocator, OggReadOptions options)
        {
            this.source = source;
            this.allocator = allocator;
            this.options = options;
            pages = new(source, allocator, options);
        }

        public ContainerInfo Info { get; private set; } = null!;
        public IReadOnlyList<EncodedStreamInfo> Streams { get; private set; } = [];

        public static async ValueTask<OggReader> OpenAsync(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            OggReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            OggReader reader = new(source, allocator, options);
            bool success = false;
            try
            {
                await reader.InitializeAsync(cancellationToken).ConfigureAwait(false);
                success = true;
                return reader;
            }
            finally { if (!success) { await reader.DisposeAsync().ConfigureAwait(false); } }
        }

        private async ValueTask InitializeAsync(CancellationToken token)
        {
            Dictionary<(int Link, uint Serial), OggTrack> active = [];
            await using OggReadCursor cursor = new(source);
            await using (OggPacketReader packets = new(cursor, allocator, options))
            {
                while (await packets.ReadPacketAsync(token).ConfigureAwait(false) is OggPacket packet)
                {
                    using (packet)
                    {
                        if (!active.TryGetValue((packet.LinkIndex, packet.Serial), out OggTrack? track))
                        {
                            track = OggTrack.Create(tracks.Count, packet, allocator);
                            bool added = false;
                            try { tracks.Add(track); added = true; }
                            finally { if (!added) { track.Dispose(); } }
                            active.Add((packet.LinkIndex, packet.Serial), track);
                        }
                        else { track.Add(packet, token); }
                    }
                }
            }
            List<EncodedStreamInfo> streams = [];
            List<ContainerStreamInfo> facts = [];
            List<MediaMetadata> metadata = [];
            foreach (OggTrack track in tracks)
            {
                token.ThrowIfCancellationRequested();
                (EncodedStreamInfo stream, ContainerStreamInfo info, MediaMetadata native) = track.Complete();
                streams.Add(stream);
                facts.Add(info);
                metadata.Add(native);
            }
            Streams = streams.AsReadOnly();
            Info = new("ogg", tracks.Count == 1 ? facts[0].DeclaredDuration : null, tags: [], streamInfo: facts,
                metadata: new("media", children: metadata));
        }

        public long GetPacketCount(int streamIndex)
        {
            Enter();
            try { return Track(streamIndex).Index.Count; }
            finally { Exit(); }
        }

        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                OggTrack track = Track(streamIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(beforePacketIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(beforePacketIndex, track.Index.Count);
                target.TimeBase.Validate();
                if (beforePacketIndex == 0) { return null; }
                long low = 0, high = beforePacketIndex - 1;
                while (low < high)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long middle = low + (high - low + 1) / 2;
                    MediaTimestamp timestamp = new(track.Index[middle].Timestamp, track.Clock);
                    if (timestamp.CompareTo(target) <= 0) { low = middle; }
                    else { high = middle - 1; }
                }
                cancellationToken.ThrowIfCancellationRequested();
                low = track.RandomAccessPacket(low);
                return new(streamIndex, low, new(track.Index[low].Timestamp, track.Clock));
            }
            finally { Exit(); }
        }

        public ContainerSeekPoint GetPrerollStart(int streamIndex, long outputPacketIndex, int minimumPrecedingPackets,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                OggTrack track = Track(streamIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(minimumPrecedingPackets);
                OggIndexedPacket output = track.Index[outputPacketIndex];
                long start = Math.Max(0, outputPacketIndex - Math.Max(minimumPrecedingPackets, track.MinimumSeekPackets));
                Int128 cutoff = (Int128)output.Timestamp - track.SeekPreroll;
                while (start > 0 && track.Index[start].Timestamp > cutoff)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    start--;
                }
                cancellationToken.ThrowIfCancellationRequested();
                start = track.RandomAccessPacket(start);
                return new(streamIndex, start, new(track.Index[start].Timestamp, track.Clock));
            }
            finally { Exit(); }
        }

        public ContainerPacketTiming GetPacketTiming(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                OggTrack track = Track(streamIndex); OggIndexedPacket entry = track.Index[packetIndex];
                MediaTimestamp timestamp = new(entry.Timestamp, track.Clock);
                return new(timestamp, timestamp, new(entry.Samples, track.Clock), IsKeyFrame(track, entry));
            }
            finally { Exit(); }
        }

        private static bool IsKeyFrame(OggTrack track, OggIndexedPacket entry) =>
            entry.RandomAccessPacket < 0 || track.Index[entry.RandomAccessPacket].Timestamp == entry.Timestamp;

        public MediaDuration? GetPresentationDuration(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                OggTrack track = Track(streamIndex);
                return new(track.Index[packetIndex].PresentationSamples, track.Clock);
            }
            finally { Exit(); }
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                OggTrack? selected = null;
                OggIndexedPacket best = default;
                foreach (OggTrack track in tracks)
                {
                    if (track.NextPacket == track.Index.Count) { continue; }
                    OggIndexedPacket candidate = track.Index[track.NextPacket];
                    if (selected is null || candidate.Page < best.Page || candidate.Page == best.Page && candidate.Lace < best.Lace)
                    {
                        selected = track;
                        best = candidate;
                    }
                }
                if (selected is null) { return null; }
                MediaPacket packet = await ReadCoreAsync(selected, best, cancellationToken).ConfigureAwait(false);
                selected.NextPacket++;
                return packet;
            }
            finally { Exit(); }
        }

        public async ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                OggTrack track = Track(streamIndex);
                return await ReadCoreAsync(track, track.Index[packetIndex], cancellationToken).ConfigureAwait(false);
            }
            finally { Exit(); }
        }

        private async ValueTask<MediaPacket> ReadCoreAsync(OggTrack track, OggIndexedPacket entry, CancellationToken token)
        {
            using MediaBuffer data = await OggPacketAccess.ReadAsync(pages, track, entry, allocator, token).ConfigureAwait(false);
            MediaTimestamp timestamp = new(entry.Timestamp, track.Clock);
            return new(track.StreamIndex, data, timestamp, timestamp, new(entry.Samples, track.Clock), IsKeyFrame(track, entry));
        }

        private OggTrack Track(int index)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, tracks.Count);
            return tracks[index];
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Ogg reader operations must not overlap."); }
        }

        private void Exit() => Volatile.Write(ref busy, 0);

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    pages.Dispose();
                    foreach (OggTrack track in tracks) { track.Dispose(); }
                    tracks.Clear();
                }
                return ValueTask.CompletedTask;
            }
            finally { Exit(); }
        }
    }
}
