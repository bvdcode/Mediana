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
        private async ValueTask SeekWithPrerollAsync(IndexedPacketReader indexed, IPacketPrerollDecoder preroll,
            MediaTimestamp target, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int required = preroll.RequiredPrerollPackets;
            if (required < 0)
            {
                throw new InvalidOperationException("The decoder returned a negative preroll requirement.");
            }
            ContainerSeekPoint output = indexed.FindSeekPoint(target, indexed.Count, cancellationToken)
                ?? throw new NotSupportedException("No packet is available for the requested seek.");
            if (output.StreamIndex != streamIndex || output.PacketIndex < 0 || output.PacketIndex >= indexed.Count ||
                (output.PacketIndex != 0 && output.PresentationTime.CompareTo(target) > 0))
            {
                throw new InvalidDataException("The container returned an invalid output seek candidate.");
            }
            ContainerSeekPoint start = indexed.GetPrerollStart(output.PacketIndex, required, cancellationToken);
            if (start.StreamIndex != streamIndex || start.PacketIndex < 0 || start.PacketIndex > output.PacketIndex ||
                (start.PacketIndex != 0 && output.PacketIndex - start.PacketIndex < required))
            {
                throw new InvalidDataException("The container returned an invalid preroll position.");
            }
            CountPacketRead();
            using MediaPacket packet = await indexed.ReadPacketAtAsync(start.PacketIndex, cancellationToken).ConfigureAwait(false);
            if (packet.StreamIndex != streamIndex || packet.PresentationTime != start.PresentationTime)
            {
                throw new InvalidDataException("The preroll packet differs from its container index.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            preroll.ResetForPreroll(output.PacketIndex - start.PacketIndex);
            pendingPacket?.Dispose();
            pendingPacket = null;
            pendingPacket = packet.Retain();
            indexed.Reposition(start.PacketIndex + 1);
            inputCompleted = false;
            ended = false;
        }
    }
}
