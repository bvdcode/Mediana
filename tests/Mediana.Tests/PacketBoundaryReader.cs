// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal class PacketBoundaryReader : IIndexedPacketTimingReader
    {
        private readonly IIndexedPacketTimingReader reader;

        public PacketBoundaryReader(IIndexedPacketTimingReader reader, MediaKind kind, EncodedPacketBoundary boundary)
        {
            this.reader = reader;
            EncodedStreamInfo original = reader.Streams.Single();
            Streams = [new(original.StreamIndex, kind, original.CodecId,
                new("test.configuration", "test.packet", boundary), original.TimeBase)];
        }

        public ContainerInfo Info => reader.Info;
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }
        public long GetPacketCount(int streamIndex) => reader.GetPacketCount(streamIndex);
        public ContainerPacketTiming GetPacketTiming(int streamIndex, long packetIndex, CancellationToken cancellationToken = default) =>
            reader.GetPacketTiming(streamIndex, packetIndex, cancellationToken);
        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default) => reader.FindSeekPoint(streamIndex, target, beforePacketIndex, cancellationToken);
        public ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex, CancellationToken cancellationToken = default) =>
            reader.ReadPacketAtAsync(streamIndex, packetIndex, cancellationToken);
        public ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default) => reader.ReadPacketAsync(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
