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
        private readonly IIndexedPresentationTimingReader? presentationTiming;
        private readonly IIndexedDecodeTimingReader? decodeTiming;
        private readonly int streamIndex;
        private readonly SemaphoreSlim? gate;
        private long position;

        public IndexedPacketReader(IIndexedContainerReader source, int streamIndex, SemaphoreSlim? gate = null,
            bool usePresentationTiming = true, bool useDecodeTiming = false)
        {
            this.source = source;
            presentationTiming = usePresentationTiming ? source as IIndexedPresentationTimingReader : null;
            decodeTiming = useDecodeTiming ? source as IIndexedDecodeTimingReader : null;
            this.streamIndex = streamIndex;
            this.gate = gate;
            Count = source.GetPacketCount(streamIndex);
            if (Count < 0)
            {
                throw new InvalidDataException("An indexed container returned a negative packet count.");
            }
        }

        public ContainerInfo Info => source.Info;
        public long Count { get; }
        public bool SupportsPreroll => source is IIndexedPrerollReader;
        public IReadOnlyList<EncodedStreamInfo> Streams => source.Streams;

        public ContainerSeekPoint? FindSeekPoint(MediaTimestamp target, long beforePacketIndex, CancellationToken cancellationToken) =>
            source.FindSeekPoint(streamIndex, target, beforePacketIndex, cancellationToken);

        public ContainerSeekPoint GetPrerollStart(long outputPacketIndex, int minimumPrecedingPackets,
            CancellationToken cancellationToken) => source is IIndexedPrerollReader preroll
                ? preroll.GetPrerollStart(streamIndex, outputPacketIndex, minimumPrecedingPackets, cancellationToken)
                : throw new NotSupportedException("The indexed reader does not provide codec preroll positioning.");

        public async ValueTask<MediaPacket> ReadPacketAtAsync(long packetIndex, CancellationToken cancellationToken)
        {
            if (gate is not null)
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            try
            {
                MediaDuration? duration = presentationTiming?.GetPresentationDuration(streamIndex, packetIndex, cancellationToken);
                MediaTimestamp? decodeTime = decodeTiming?.GetDecodeTime(streamIndex, packetIndex, cancellationToken);
                MediaPacket packet = await source.ReadPacketAtAsync(streamIndex, packetIndex, cancellationToken).ConfigureAwait(false);
                if (packet.DecodeTime is null && decodeTime is MediaTimestamp time)
                {
                    using (packet)
                    {
                        return packet.RetainWithTiming(packet.PresentationTime, time, duration ?? packet.Duration);
                    }
                }
                return presentationTiming is null ? packet : PacketPresentationTiming.Apply(packet, duration);
            }
            finally
            {
                gate?.Release();
            }
        }

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
