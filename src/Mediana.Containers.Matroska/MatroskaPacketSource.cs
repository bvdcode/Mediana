// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    /// <summary>Reads owned packets in physical order or by track index. Borrows the source and completed index.
    /// Owns a reusable block workspace; operations and disposal cannot overlap.</summary>
    internal class MatroskaPacketSource : IDisposable
    {
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly MatroskaPacketIndex index;
        private readonly MatroskaBlockReader blocks;
        private readonly MatroskaTrack[] tracks;
        private readonly MatroskaCodecConfiguration[] configurations;
        private readonly MatroskaTrackClock[] clocks;
        private readonly MediaDuration?[] framePeriods;
        private int cachedBlock = -1;
        private int nextBlock;
        private int nextFrame;
        private int busy;
        private bool disposed;

        public MatroskaPacketSource(IRandomAccessByteSource source, MediaBufferAllocator allocator, MatroskaPacketIndex index,
            IReadOnlyList<MatroskaTrack> tracks, IReadOnlyList<MatroskaCodecConfiguration> configurations,
            ulong timestampScale, MatroskaIndexOptions options)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(index);
            ArgumentNullException.ThrowIfNull(tracks);
            ArgumentNullException.ThrowIfNull(configurations);
            ArgumentOutOfRangeException.ThrowIfZero(timestampScale);
            options.Validate();
            if (tracks.Count != index.TrackCount || configurations.Count != tracks.Count)
            {
                throw new ArgumentException("The Matroska tracks, configurations and packet index must have the same declaration order.", nameof(configurations));
            }
            this.source = source;
            this.allocator = allocator;
            this.index = index;
            this.tracks = tracks.ToArray();
            this.configurations = configurations.ToArray();
            clocks = new MatroskaTrackClock[tracks.Count];
            framePeriods = new MediaDuration?[tracks.Count];
            for (int stream = 0; stream < tracks.Count; stream++)
            {
                if (configurations[stream].Stream.StreamIndex != stream)
                {
                    throw new ArgumentException("Matroska stream indices must follow track declaration order.", nameof(configurations));
                }
                clocks[stream] = new(timestampScale, tracks[stream].Timing);
                MediaDuration? period = configurations[stream].FrameDuration;
                if (period is null && tracks[stream].Timing.DefaultDurationNanoseconds is ulong nanoseconds)
                {
                    if (nanoseconds > long.MaxValue)
                    {
                        throw new NotSupportedException("The Matroska default frame duration exceeds the supported nanosecond range.");
                    }
                    period = new((long)nanoseconds, MatroskaTrackClock.TimeBase);
                }
                framePeriods[stream] = period;
            }
            blocks = new(source, allocator, options.MaximumBlockHeaderBytes, options.MaximumFrameBytes);
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken token = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                token.ThrowIfCancellationRequested();
                if (nextBlock == index.BlockCount)
                {
                    return null;
                }
                MediaPacket packet = await ReadAsync(new(nextBlock, nextFrame), token).ConfigureAwait(false);
                nextFrame++;
                if (nextFrame == index.GetBlock(nextBlock).FrameCount)
                {
                    nextBlock++;
                    nextFrame = 0;
                }
                return packet;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public async ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex, CancellationToken token = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                token.ThrowIfCancellationRequested();
                return await ReadAsync(index.Locate(streamIndex, packetIndex), token).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<MediaPacket> ReadAsync(MatroskaPacketAddress address, CancellationToken token)
        {
            MatroskaIndexedBlock block = index.GetBlock(address.BlockIndex);
            MatroskaBlockExtensions? extra = index.GetExtensions(address.BlockIndex);
            if ((block.Flags & 8) != 0 || extra?.CodecState is not null || extra?.BlockAdditions is not null ||
                extra?.DiscardPaddingNanoseconds is not (null or 0))
            {
                throw new NotSupportedException("Matroska invisible frames, codec state, block additions and discard padding require additional packet semantics.");
            }
            if (cachedBlock != address.BlockIndex)
            {
                cachedBlock = -1;
                MatroskaBlockHeader header = await blocks.ReadAsync(block.Element, token).ConfigureAwait(false);
                if (header.TrackNumber != tracks[block.StreamIndex].Number || header.FrameCount != block.FrameCount ||
                    header.RelativeTimestamp != block.RelativeTimestamp || header.Flags != block.Flags)
                {
                    throw new InvalidDataException("The Matroska block no longer matches its index.");
                }
                cachedBlock = address.BlockIndex;
            }
            MatroskaFrameRange frame = blocks.GetFrame(address.FrameIndex);
            MatroskaPacketTiming timing = GetTiming(address);
            using WritableMediaBuffer writable = allocator.Allocate(Math.Max(1, frame.Length));
            await source.ReadExactlyAtAsync(frame.Offset, writable.Memory[..frame.Length], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            configurations[block.StreamIndex].ValidatePacket(writable.Memory.Span[..frame.Length]);
            using MediaBuffer buffer = writable.Publish();
            using MediaBuffer payload = buffer.Slice(0, frame.Length);
            return new(block.StreamIndex, payload, timing.PresentationTime, duration: timing.Duration, isKeyFrame: timing.IsKeyFrame);
        }

        public ContainerPacketTiming GetPacketTiming(int streamIndex, long packetIndex, CancellationToken token)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                token.ThrowIfCancellationRequested();
                MatroskaPacketTiming timing = GetTiming(index.Locate(streamIndex, packetIndex));
                return new(timing.PresentationTime, null, timing.Duration, timing.IsKeyFrame);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken token = default)
        {
            target.TimeBase.Validate();
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                token.ThrowIfCancellationRequested();
                long count = index.GetPacketCount(streamIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(beforePacketIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(beforePacketIndex, count);
                long position = beforePacketIndex - 1;
                while (position >= 0)
                {
                    token.ThrowIfCancellationRequested();
                    MatroskaPacketAddress address = index.Locate(streamIndex, position);
                    MatroskaIndexedBlock block = index.GetBlock(address.BlockIndex);
                    bool sync = block.IsSimpleBlock ? (block.Flags & 0x80) != 0 : !block.HasReferences;
                    long firstPacket = position - address.FrameIndex;
                    if (!sync && firstPacket != 0)
                    {
                        position = firstPacket - 1;
                        continue;
                    }
                    for (int frame = address.FrameIndex; frame >= 0; frame--, position--)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!sync && position != 0)
                        {
                            continue;
                        }
                        MatroskaPacketTiming timing = GetTiming(new(address.BlockIndex, frame));
                        if (timing.PresentationTime is MediaTimestamp pts && (position == 0 || pts.CompareTo(target) <= 0))
                        {
                            return new(streamIndex, position, pts);
                        }
                    }
                }
                return null;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private MatroskaPacketTiming GetTiming(MatroskaPacketAddress address)
        {
            MatroskaIndexedBlock block = index.GetBlock(address.BlockIndex);
            MatroskaBlockExtensions? extra = index.GetExtensions(address.BlockIndex);
            MatroskaTrackClock clock = clocks[block.StreamIndex];
            MediaDuration? period = framePeriods[block.StreamIndex];
            MediaTimestamp? pts = null;
            if (address.FrameIndex == 0)
            {
                pts = clock.GetPresentationTime(index.GetClusterTimestamp(block.ClusterIndex), block.RelativeTimestamp);
            }
            else if (period is MediaDuration step)
            {
                if (step.Value > long.MaxValue / address.FrameIndex)
                {
                    throw new NotSupportedException("The Matroska contiguous frame offset exceeds the supported duration range.");
                }
                MediaDuration offset = new(step.Value * address.FrameIndex, step.TimeBase);
                pts = clock.GetPresentationTime(index.GetClusterTimestamp(block.ClusterIndex), block.RelativeTimestamp, offset);
            }
            MediaDuration? duration = block.FrameCount == 1 && extra?.DurationTicks is ulong ticks ? clock.GetTrackDuration(ticks) : period;
            bool keyFrame = block.IsSimpleBlock ? (block.Flags & 0x80) != 0 : !block.HasReferences;
            return new(pts, duration, keyFrame);
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent Matroska packet operations are not supported.");
            }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                disposed = true;
                blocks.Dispose();
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }
    }
}
