// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Cli
{
    internal class HlsAacPacketBuffer(AacEncoder encoder) : IContainerWriter
    {
        private const int MaximumPackets = 128;
        private readonly Queue<MediaPacket> packets = [];
        private MediaTimestamp? lastTimestamp;
        private bool completed;
        private bool disposed;
        public AacEncoder Encoder { get; } = encoder;
        public MediaPacket? Next => packets.TryPeek(out MediaPacket? packet) ? packet : null;

        public ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed || packets.Count == MaximumPackets)
            {
                throw new InvalidOperationException("The AAC output is complete or its pending packet budget is exhausted.");
            }
            if (packet.StreamIndex != Encoder.Stream.StreamIndex || packet.DecodeTime is not MediaTimestamp timestamp
                || (lastTimestamp is MediaTimestamp previous && timestamp.CompareTo(previous) <= 0))
            {
                throw new InvalidDataException("AAC variants require known strictly increasing decode times.");
            }
            packets.Enqueue(packet.Retain());
            lastTimestamp = timestamp;
            return ValueTask.CompletedTask;
        }

        public MediaPacket Take() => packets.Dequeue();

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            completed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                while (packets.TryDequeue(out MediaPacket? packet))
                {
                    packet.Dispose();
                }
                Encoder.Dispose();
            }
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
