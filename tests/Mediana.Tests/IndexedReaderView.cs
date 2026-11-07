// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal class IndexedReaderView(IIndexedContainerReader reader) : IIndexedContainerReader
    {
        public ContainerInfo Info => reader.Info;
        public IReadOnlyList<EncodedStreamInfo> Streams => reader.Streams;
        public long GetPacketCount(int streamIndex) => reader.GetPacketCount(streamIndex);
        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default) => reader.FindSeekPoint(streamIndex, target, beforePacketIndex, cancellationToken);
        public ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default) => reader.ReadPacketAtAsync(streamIndex, packetIndex, cancellationToken);
        public ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default) => reader.ReadPacketAsync(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
