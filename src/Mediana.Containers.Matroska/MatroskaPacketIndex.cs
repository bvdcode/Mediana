// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Matroska
{
    /// <summary>Native block index in source order with independent per-track packet lookup.
    /// Owns allocator-backed pages. Read access begins after completion; disposal must not overlap reads.</summary>
    internal class MatroskaPacketIndex : IDisposable
    {
        private readonly MatroskaIndexTable<MatroskaIndexedBlock> blocks;
        private readonly MatroskaIndexTable<ulong> clusters;
        private readonly MatroskaIndexTable<MatroskaBlockExtensions> extensions;
        private readonly MatroskaTrackIndex[] tracks;
        private readonly Dictionary<ulong, int> trackNumbers;
        private readonly MatroskaIndexOptions options;
        private readonly MatroskaIndexBudget budget;
        private bool complete;
        private bool disposed;

        public MatroskaPacketIndex(MediaBufferAllocator allocator, IReadOnlyList<MatroskaTrack> tracks, MatroskaIndexOptions options)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(tracks);
            options.Validate();
            this.options = options;
            budget = new(allocator, options.MaximumIndexBytes);
            blocks = new(budget);
            clusters = new(budget);
            extensions = new(budget);
            this.tracks = new MatroskaTrackIndex[tracks.Count];
            trackNumbers = new(tracks.Count);
            for (int index = 0; index < tracks.Count; index++)
            {
                MatroskaTrack track = tracks[index];
                ArgumentNullException.ThrowIfNull(track);
                if (track.Number == 0 || !trackNumbers.TryAdd(track.Number, index))
                {
                    throw new ArgumentException("The native Matroska tracks require distinct positive numbers.", nameof(tracks));
                }
                this.tracks[index] = new(track, budget);
            }
        }

        public int BlockCount { get { EnsureReadable(); return blocks.Count; } }
        public int ClusterCount { get { EnsureReadable(); return clusters.Count; } }
        public int TrackCount { get { EnsureReadable(); return tracks.Length; } }

        internal WritableMediaBuffer AllocatePresentationStorage(int length)
        {
            EnsureReadable();
            return budget.Allocate(length);
        }

        internal void AddCluster(ulong timestamp)
        {
            EnsureBuilding();
            clusters.Add(timestamp);
        }

        internal void AddBlock(EbmlElement block, MatroskaBlockHeader header, bool hasReferences,
            MatroskaBlockExtensions extra)
        {
            EnsureBuilding();
            if (blocks.Count == options.MaximumBlocks)
            {
                throw new NotSupportedException("Matroska exceeds the configured block count.");
            }
            if (!trackNumbers.TryGetValue(header.TrackNumber, out int streamIndex))
            {
                throw new InvalidDataException("A Matroska block references an undeclared track number.");
            }
            MatroskaTrackIndex track = tracks[streamIndex];
            if (header.FrameCount > options.MaximumPacketsPerTrack - track.PacketCount)
            {
                throw new NotSupportedException("Matroska exceeds the configured per-track packet count.");
            }
            if (!track.Track.Flags.Lacing && header.Lacing != MatroskaLacingMode.None)
            {
                throw new InvalidDataException("A Matroska block uses lacing forbidden by its track.");
            }
            if (!header.IsSimpleBlock && track.Track.Timing.DefaultDurationNanoseconds is not null && extra.DurationTicks is null)
            {
                throw new InvalidDataException("A Matroska BlockGroup with a track DefaultDuration requires BlockDuration.");
            }
            long end = block.EndOffset ?? throw new InvalidDataException("An indexed Matroska block requires a known extent.");
            long payloadBytes = end - header.PayloadOffset;
            if (payloadBytes < 0)
            {
                throw new InvalidDataException("The Matroska block payload exceeds its extent.");
            }
            int extensionIndex = -1;
            if (extra.HasValues)
            {
                extensionIndex = extensions.Count;
                extensions.Add(extra);
            }
            track.Blocks.Add(new(track.PacketCount, blocks.Count));
            blocks.Add(new(block.DataOffset, end, clusters.Count, streamIndex, extensionIndex, header.RelativeTimestamp,
                checked((ushort)header.FrameCount), header.Flags, header.IsSimpleBlock, hasReferences));
            track.PacketCount = checked(track.PacketCount + header.FrameCount);
            track.PayloadBytes = checked(track.PayloadBytes + payloadBytes);
        }

        internal void Complete()
        {
            EnsureBuilding();
            complete = true;
        }

        public MatroskaIndexedBlock GetBlock(int index)
        {
            EnsureReadable();
            return blocks.Get(index);
        }

        public ulong GetClusterTimestamp(int index)
        {
            EnsureReadable();
            return clusters.Get(index);
        }

        public MatroskaBlockExtensions? GetExtensions(int blockIndex)
        {
            MatroskaIndexedBlock block = GetBlock(blockIndex);
            if (block.ExtensionsIndex < 0)
            {
                return null;
            }
            return extensions.Get(block.ExtensionsIndex);
        }

        public long GetPacketCount(int streamIndex) => GetTrack(streamIndex).PacketCount;
        public long GetPayloadBytes(int streamIndex) => GetTrack(streamIndex).PayloadBytes;

        public MatroskaPacketAddress Locate(int streamIndex, long packetIndex)
        {
            MatroskaTrackIndex track = GetTrack(streamIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(packetIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(packetIndex, track.PacketCount);
            int low = 0;
            int high = track.Blocks.Count;
            while (low + 1 < high)
            {
                int middle = low + (high - low) / 2;
                if (track.Blocks.Get(middle).FirstPacketIndex <= packetIndex)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            MatroskaTrackBlock location = track.Blocks.Get(low);
            return new(location.BlockIndex, checked((int)(packetIndex - location.FirstPacketIndex)));
        }

        private MatroskaTrackIndex GetTrack(int streamIndex)
        {
            EnsureReadable();
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, tracks.Length);
            return tracks[streamIndex];
        }

        private void EnsureReadable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!complete)
            {
                throw new InvalidOperationException("The Matroska packet index is incomplete.");
            }
        }

        private void EnsureBuilding()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (complete)
            {
                throw new InvalidOperationException("The Matroska packet index is already complete.");
            }
        }

        public void Dispose()
        {
            disposed = true;
            blocks.Dispose();
            clusters.Dispose();
            extensions.Dispose();
            foreach (MatroskaTrackIndex track in tracks)
            {
                track.Blocks.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
