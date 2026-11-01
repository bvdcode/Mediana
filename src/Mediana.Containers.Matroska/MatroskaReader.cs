// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    /// <summary>Indexes one known-length Matroska Segment. Borrows its random-access source.
    /// Packets follow stored Block order, with exact rounded PTS and no inferred DTS.
    /// Returned packets and metadata survive reader disposal. Operations and disposal cannot overlap.</summary>
    public partial class MatroskaReader : IIndexedContainerReader, IContainerAttachmentReader
    {
        private readonly MatroskaPacketIndex index;
        private readonly MatroskaPacketSource packets;
        private readonly MatroskaAttachmentSource attachmentSource;
        private int busy;
        private bool disposed;

        private MatroskaReader(MatroskaPacketIndex index, MatroskaPacketSource packets,
            MatroskaSegmentInfo segmentInfo, IReadOnlyList<MatroskaTrackInfo> tracks, ContainerInfo info,
            MatroskaAttachmentSource attachmentSource)
        {
            this.index = index;
            this.packets = packets;
            this.attachmentSource = attachmentSource;
            SegmentInfo = segmentInfo;
            Tracks = tracks;
            Streams = Array.AsReadOnly(tracks.Select(track => track.Stream).ToArray());
            Info = info;
        }

        public ContainerInfo Info { get; }
        public MatroskaSegmentInfo SegmentInfo { get; }
        public IReadOnlyList<MatroskaTrackInfo> Tracks { get; }
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }
        public IReadOnlyList<MatroskaAttachmentInfo> Attachments => attachmentSource.Attachments;
        IReadOnlyList<ContainerAttachmentInfo> IContainerAttachmentReader.Attachments => Attachments;

        public async ValueTask<MediaBuffer> ReadAttachmentAsync(int attachmentIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return await attachmentSource.ReadAsync(attachmentIndex, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public long GetPacketCount(int streamIndex)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return index.GetPacketCount(streamIndex);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return packets.FindSeekPoint(streamIndex, target, beforePacketIndex, cancellationToken);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return await packets.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public async ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return await packets.ReadPacketAtAsync(streamIndex, packetIndex, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent Matroska reader operations are not supported.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                packets.Dispose();
                index.Dispose();
                return ValueTask.CompletedTask;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
