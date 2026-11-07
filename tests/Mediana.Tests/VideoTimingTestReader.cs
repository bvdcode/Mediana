// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal class VideoTimingTestReader(IReadOnlyList<ContainerPacketTiming> timings, TimeBase clock, EncodedStreamInfo? stream = null) : IIndexedPacketTimingReader
    {
        public ContainerInfo Info { get; } = new("indexed-video");
        public IReadOnlyList<EncodedStreamInfo> Streams { get; } = [stream ?? new(7, MediaKind.Video, "video",
            new("video", "access-unit", EncodedPacketBoundary.AccessUnit), clock)];
        public Action? AfterRead { get; set; }
        public int MetadataReads { get; private set; }
        public int PacketReads { get; private set; }

        public long GetPacketCount(int streamIndex)
        {
            if (streamIndex != Streams[0].StreamIndex) { throw new ArgumentOutOfRangeException(nameof(streamIndex)); }
            return timings.Count;
        }

        public ContainerPacketTiming GetPacketTiming(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            _ = GetPacketCount(streamIndex);
            cancellationToken.ThrowIfCancellationRequested();
            ContainerPacketTiming result = timings[checked((int)packetIndex)];
            MetadataReads++;
            AfterRead?.Invoke();
            return result;
        }

        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Planning must not seek a decoder.");

        public ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            PacketReads++;
            throw new InvalidOperationException("Planning must not read compressed payload.");
        }

        public ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            PacketReads++;
            throw new InvalidOperationException("Planning must not read compressed payload.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
