// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.IO;

namespace Mediana.Containers.MpegTs
{
    internal class MpegSystemsReader : IContainerReader, IContainerStreamMetadataProvider
    {
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly List<MpegSystemsPacket> packets;
        private readonly MpegSystemsTrack[] tracks;
        private int position;
        private int busy;
        private bool disposed;

        private MpegSystemsReader(IRandomAccessByteSource source, MediaBufferAllocator allocator, string formatId,
            IReadOnlyCollection<MpegSystemsTrack> tracks, List<MpegSystemsPacket> packets)
        {
            this.source = source;
            this.allocator = allocator;
            this.packets = packets;
            packets.Sort(static (left, right) => left.Position.CompareTo(right.Position));
            this.tracks = tracks.OrderBy(track => track.Stream.StreamIndex).ToArray();
            Streams = Array.AsReadOnly(this.tracks.Select(track => track.Stream).ToArray());
            Info = new(formatId, streamInfo: this.tracks.Select(track => track.Describe()));
        }

        public ContainerInfo Info { get; }
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }

        public static async ValueTask<IContainerReader> CreateAsync(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            string formatId, IReadOnlyCollection<MpegSystemsTrack> tracks, List<MpegSystemsPacket> packets, CancellationToken token)
        {
            using WritableMediaBuffer prefix = allocator.Allocate(16 * 1024);
            foreach (MpegSystemsTrack track in tracks)
            {
                int limit = track.Stream.Kind == MediaKind.Video ? prefix.Memory.Length : 64;
                int length = 0;
                foreach (MpegSystemsPacket packet in packets)
                {
                    if (packet.StreamIndex != track.Stream.StreamIndex) { continue; }
                    foreach (MpegPayloadRange range in packet.Ranges)
                    {
                        int count = Math.Min(range.Length, limit - length);
                        await source.ReadExactlyAtAsync(range.Offset, prefix.Memory.Slice(length, count), token).ConfigureAwait(false);
                        length += count;
                        if (length == limit) { break; }
                    }
                    if (length == limit) { break; }
                }
                track.ReadOpeningMetadata(prefix.Span[..length]);
            }
            return new MpegSystemsReader(source, allocator, formatId, tracks, packets);
        }

        public StreamMetadata? GetStreamMetadata(int streamIndex)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, tracks.Length);
            return tracks[streamIndex].Metadata;
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (position == packets.Count) { return null; }

                MpegSystemsPacket entry = packets[position];
                using WritableMediaBuffer destination = allocator.Allocate(entry.Length);
                int offset = 0;
                foreach (MpegPayloadRange range in entry.Ranges)
                {
                    await source.ReadExactlyAtAsync(range.Offset, destination.Memory.Slice(offset, range.Length), cancellationToken).ConfigureAwait(false);
                    offset += range.Length;
                }
                using MediaBuffer payload = destination.Publish();
                MediaPacket packet = new(entry.StreamIndex, payload, entry.PresentationTime, entry.DecodeTime, isKeyFrame: entry.RandomAccess);
                position++;
                return packet;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                packets.Clear();
                return ValueTask.CompletedTask;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent MPEG systems reader operations are not supported.");
            }
        }

        public static IRandomAccessByteSource RequireRandomAccess(IByteSource source) =>
            source is IRandomAccessByteSource { Length: not null } random ? random :
                throw new NotSupportedException("MPEG systems demultiplexing requires a known-length random access source.");
    }
}
