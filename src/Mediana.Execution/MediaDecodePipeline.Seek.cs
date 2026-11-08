// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    public partial class MediaDecodePipeline<TFrame>
    {
        public bool CanSeek => reader is IndexedPacketReader indexed &&
            (randomAccessInspector is not null || (indexed.SupportsPreroll && decoder is IPacketPrerollDecoder));

        /// <summary>Restarts at a container candidate at or before target using codec inspection or required preroll,
        /// or at the first packet when target precedes its PTS. Preroll output is suppressed by the decoder;
        /// other frames before target remain available. Read budgets remain cumulative across seeks.</summary>
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
                if (reader is not IndexedPacketReader indexed)
                {
                    throw new NotSupportedException("Seeking requires indexed container packets.");
                }
                await InspectOriginAsync(indexed, cancellationToken).ConfigureAwait(false);
                if (decoder is IPacketPrerollDecoder preroll && indexed.SupportsPreroll)
                {
                    await SeekWithPrerollAsync(indexed, preroll, target, cancellationToken).ConfigureAwait(false);
                    succeeded = true;
                    return;
                }
                if (randomAccessInspector is null)
                {
                    throw new NotSupportedException("Seeking requires codec restart inspection or codec and container preroll support.");
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
                    RandomAccessRecovery? recovery = InspectRestart(packet, cancellationToken);
                    if (recovery.HasValue)
                    {
                        if (recovery.Value.RequiredPrerollPackets != 0)
                        {
                            throw new NotSupportedException("This codec restart requires decoder and container preroll support.");
                        }
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

        private RandomAccessRecovery? InspectRestart(MediaPacket packet, CancellationToken cancellationToken)
        {
            if (randomAccessInspector is IRandomAccessRecoveryInspector recovery)
            {
                return recovery.InspectRecovery(packet, cancellationToken);
            }
            return randomAccessInspector is null || randomAccessInspector.IsRandomAccess(packet, cancellationToken)
                ? new RandomAccessRecovery(0) : null;
        }

        private async ValueTask InspectOriginAsync(IndexedPacketReader indexed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (originInspected || randomAccessInspector is not IRandomAccessOriginInspector origin || !origin.RequiresOriginPacket)
            {
                return;
            }
            if (indexed.Count == 0)
            {
                throw new NotSupportedException("The empty stream has no opening packet for restart inspection.");
            }
            CountPacketRead();
            using MediaPacket packet = await indexed.ReadPacketAtAsync(0, cancellationToken).ConfigureAwait(false);
            if (packet.StreamIndex != streamIndex)
            {
                throw new InvalidDataException("The opening packet differs from its indexed stream.");
            }
            origin.InspectOrigin(packet, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            originInspected = true;
        }
    }
}
