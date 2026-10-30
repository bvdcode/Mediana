// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>A selected-stream cursor that borrows an indexed reader and preserves its original cursors.</summary>
    internal class IndexedPacketReader : IContainerReader
    {
        private readonly IIndexedContainerReader source;
        private readonly int streamIndex;
        private long position;

        public IndexedPacketReader(IIndexedContainerReader source, int streamIndex)
        {
            this.source = source;
            this.streamIndex = streamIndex;
            Count = source.GetPacketCount(streamIndex);
            if (Count < 0)
            {
                throw new InvalidDataException("An indexed container returned a negative packet count.");
            }
        }

        public long Count { get; }
        public IReadOnlyList<EncodedStreamInfo> Streams => source.Streams;

        public ContainerSeekPoint? FindSeekPoint(MediaTimestamp target, long beforePacketIndex, CancellationToken cancellationToken) =>
            source.FindSeekPoint(streamIndex, target, beforePacketIndex, cancellationToken);

        public ValueTask<MediaPacket> ReadPacketAtAsync(long packetIndex, CancellationToken cancellationToken) =>
            source.ReadPacketAtAsync(streamIndex, packetIndex, cancellationToken);

        public void Reposition(long nextPacketIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(nextPacketIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(nextPacketIndex, Count);
            position = nextPacketIndex;
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position == Count)
            {
                return null;
            }
            MediaPacket packet = await ReadPacketAtAsync(position, cancellationToken).ConfigureAwait(false);
            position++;
            return packet;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
