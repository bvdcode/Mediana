// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    /// <summary>Indexed RIFF/AVI and OpenDML reader. Borrows the source and allocator.
    /// Packet access validates the addressed chunk before publishing its payload.</summary>
    public partial class AviReader : IIndexedPacketTimingReader
    {
        private const uint Riff = 0x46464952;
        private const uint List = 0x5453494c;
        private const uint Avi = 0x20495641;
        private const uint Avix = 0x58495641;
        private const uint Movi = 0x69766f6d;
        private const uint Hdrl = 0x6c726468;
        private const uint Strl = 0x6c727473;
        private const uint Idx1 = 0x31786469;
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly WritableMediaBuffer scratch;
        private readonly List<AviTrack> tracks = [];
        private readonly List<AviChunk> movies = [];
        private readonly List<(AviChunk Index, AviChunk Movie)> oldIndexes = [];
        private long sourceLength;
        private bool disposed;
        private bool faulted;
        private int busy;

        private AviReader(IRandomAccessByteSource source, MediaBufferAllocator allocator)
        {
            this.source = source;
            this.allocator = allocator;
            scratch = allocator.Allocate(4096);
        }

        public ContainerInfo Info { get; private set; } = null!;
        public IReadOnlyList<EncodedStreamInfo> Streams { get; private set; } = [];

        public static async ValueTask<AviReader> OpenAsync(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            AviReader reader = new(source, allocator);
            bool opened = false;
            try
            {
                await reader.InitializeAsync(cancellationToken).ConfigureAwait(false);
                opened = true;
                return reader;
            }
            finally { if (!opened) { await reader.DisposeAsync().ConfigureAwait(false); } }
        }

        public long GetPacketCount(int streamIndex)
        {
            EnsureReadable();
            return Track(streamIndex).Packets.Count;
        }

        public ContainerPacketTiming GetPacketTiming(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            cancellationToken.ThrowIfCancellationRequested();
            AviTrack track = Track(streamIndex);
            AviPacketEntry entry = track.Packets[packetIndex];
            MediaTimestamp time = new(entry.Time, track.Stream.TimeBase);
            MediaTimestamp? presentation = entry.PresentationTime is long ticks ? new(ticks, track.PresentationClock) : null;
            if (track.Stream.Kind == MediaKind.Audio || track.Stream.CodecId == "mjpeg") { presentation = time; }
            return new(presentation, time, new(entry.Duration, track.Stream.TimeBase), entry.KeyFrame);
        }

        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            AviTrack track = Track(streamIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(beforePacketIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(beforePacketIndex, track.Packets.Count);
            target.TimeBase.Validate();
            long low = 0;
            long high = beforePacketIndex;
            while (low < high)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long middle = low + (high - low) / 2;
                if (new MediaTimestamp(track.Packets[middle].Time, track.Stream.TimeBase).CompareTo(target) <= 0) { low = middle + 1; }
                else { high = middle; }
            }
            ContainerSeekPoint? result = null;
            for (long index = low - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AviPacketEntry entry = track.Packets[index];
                MediaTimestamp presentation = PresentationTime(track, entry);
                if (entry.KeyFrame && presentation.CompareTo(target) <= 0)
                {
                    result = new(streamIndex, index, presentation);
                    break;
                }
            }
            for (long index = low; index < beforePacketIndex; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AviPacketEntry entry = track.Packets[index];
                if (!entry.KeyFrame) { continue; }
                MediaTimestamp presentation = PresentationTime(track, entry);
                if (presentation.CompareTo(target) > 0) { break; }
                result = new(streamIndex, index, presentation);
            }
            if (result is null && beforePacketIndex != 0 && track.Packets[0].KeyFrame &&
                PresentationTime(track, track.Packets[0]).CompareTo(target) > 0)
            {
                return new(streamIndex, 0, PresentationTime(track, track.Packets[0]));
            }
            return result;
        }

        private static MediaTimestamp PresentationTime(AviTrack track, AviPacketEntry entry) =>
            entry.PresentationTime is long ticks ? new(ticks, track.PresentationClock) : new(entry.Time, track.Stream.TimeBase);

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                EnsureReadable();
                cancellationToken.ThrowIfCancellationRequested();
                AviTrack? next = null;
                foreach (AviTrack track in tracks)
                {
                    if (track.Cursor < track.Packets.Count && (next is null || track.Packets[track.Cursor].Offset < next.Packets[next.Cursor].Offset))
                    {
                        next = track;
                    }
                }
                if (next is null) { succeeded = true; return null; }
                MediaPacket packet = await ReadPacketCoreAsync(next, next.Cursor, cancellationToken).ConfigureAwait(false);
                next.Cursor++;
                succeeded = true;
                return packet;
            }
            finally { if (!succeeded) { faulted = true; } Volatile.Write(ref busy, 0); }
        }

        public async ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                EnsureReadable();
                cancellationToken.ThrowIfCancellationRequested();
                MediaPacket packet = await ReadPacketCoreAsync(Track(streamIndex), packetIndex, cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return packet;
            }
            finally { if (!succeeded) { faulted = true; } Volatile.Write(ref busy, 0); }
        }

        private async ValueTask<MediaPacket> ReadPacketCoreAsync(AviTrack track, long packetIndex, CancellationToken cancellationToken)
        {
            AviPacketEntry entry = track.Packets[packetIndex];
            AviChunk chunk = await ReadChunkAsync(entry.Offset - 8, sourceLength, cancellationToken).ConfigureAwait(false);
            if (chunk.Id != entry.ChunkId || chunk.Length != entry.Length)
            {
                throw new InvalidDataException("The AVI packet index disagrees with its data chunk.");
            }
            using WritableMediaBuffer storage = allocator.Allocate(entry.Length);
            await source.ReadExactlyAtAsync(entry.Offset, storage.Memory, cancellationToken).ConfigureAwait(false);
            using MediaBuffer buffer = storage.Publish();
            ContainerPacketTiming timing = GetPacketTiming(track.Stream.StreamIndex, packetIndex, cancellationToken);
            return new(track.Stream.StreamIndex, buffer, timing.PresentationTime, timing.DecodeTime, timing.Duration, entry.KeyFrame);
        }

        private AviTrack Track(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, tracks.Count);
            return tracks[index];
        }

        private async ValueTask<AviChunk> ReadChunkAsync(long offset, long parentEnd, CancellationToken cancellationToken)
        {
            if (offset < 0 || parentEnd - offset < 8 || parentEnd > sourceLength)
            {
                throw new InvalidDataException("An AVI chunk header exceeds its containing extent.");
            }
            await source.ReadExactlyAtAsync(offset, scratch.Memory[..8], cancellationToken).ConfigureAwait(false);
            AviChunk chunk = new(BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span), offset,
                BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[4..]));
            if (chunk.Next > parentEnd) { throw new InvalidDataException("An AVI chunk exceeds its containing extent."); }
            return chunk;
        }

        private async ValueTask<uint> ReadListTypeAsync(AviChunk chunk, CancellationToken cancellationToken)
        {
            if (chunk.Length < 4) { throw new InvalidDataException("An AVI list is missing its type."); }
            await source.ReadExactlyAtAsync(chunk.Data, scratch.Memory[..4], cancellationToken).ConfigureAwait(false);
            return BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span);
        }

        private void EnsureReadable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The AVI reader has failed and must be disposed."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Concurrent AVI reader operations are not supported."); }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    foreach (AviTrack track in tracks) { track.Dispose(); }
                    scratch.Dispose();
                }
            }
            finally { Volatile.Write(ref busy, 0); }
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
