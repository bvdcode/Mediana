// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Execution;

namespace Mediana
{
    internal class ProcessedMediaTrack(IEncodedPacketSource source, Func<MediaTrackResult> describe) : IEncodedPacketSource
    {
        private long packets;
        private long bytes;
        public EncodedStreamInfo Stream => source.Stream;
        public MediaTrackResult Result => describe() with { Packets = packets, PayloadBytes = bytes };

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            MediaPacket? packet = await source.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
            if (packet is not null)
            {
                packets = checked(packets + 1);
                bytes = checked(bytes + packet.Data.Length);
            }
            return packet;
        }

        public ValueTask DisposeAsync() => source.DisposeAsync();
    }
}
