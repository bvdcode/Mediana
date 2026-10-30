// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    public partial class MediaDecodePipeline<TFrame>
    {
        public bool CanSeek => reader is IndexedPacketReader && randomAccessInspector is not null;

        /// <summary>Restarts at the latest codec-validated container candidate at or before target,
        /// or at the first packet when target precedes its PTS. Subsequent reads start at that point;
        /// they do not discard frames before target. Read budgets remain cumulative across seeks.</summary>
        public async ValueTask SeekAsync(MediaTimestamp target, CancellationToken cancellationToken = default)
        {
            target.TimeBase.Validate();
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The decoding pipeline has failed and must be disposed.");
                }
                if (reader is not IndexedPacketReader indexed || randomAccessInspector is null)
                {
                    throw new NotSupportedException("Seeking requires indexed container packets and codec restart inspection.");
                }
                long before = indexed.Count;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ContainerSeekPoint point = indexed.FindSeekPoint(target, before, cancellationToken)
                        ?? throw new NotSupportedException("No independently decodable packet is available for the requested seek.");
                    if (point.StreamIndex != streamIndex || point.PacketIndex < 0 || point.PacketIndex >= before ||
                        (point.PacketIndex != 0 && point.PresentationTime.CompareTo(target) > 0))
                    {
                        throw new InvalidDataException("The container returned an invalid seek candidate.");
                    }
                    CountPacketRead();
                    using MediaPacket packet = await indexed.ReadPacketAtAsync(point.PacketIndex, cancellationToken).ConfigureAwait(false);
                    if (packet.StreamIndex != streamIndex || packet.PresentationTime != point.PresentationTime)
                    {
                        throw new InvalidDataException("The seek packet differs from its container index.");
                    }
                    if (randomAccessInspector.IsRandomAccess(packet, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        decoder.Reset();
                        pendingPacket?.Dispose();
                        pendingPacket = null;
                        pendingPacket = packet.Retain();
                        indexed.Reposition(point.PacketIndex + 1);
                        inputCompleted = false;
                        ended = false;
                        succeeded = true;
                        return;
                    }
                    before = point.PacketIndex;
                }
            }
            finally
            {
                try
                {
                    if (!succeeded)
                    {
                        faulted = true;
                        DisposeDecoder();
                    }
                }
                finally
                {
                    Volatile.Write(ref busy, 0);
                }
            }
        }
    }
}
