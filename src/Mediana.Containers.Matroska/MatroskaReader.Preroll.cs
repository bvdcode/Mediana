// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    public partial class MatroskaReader : IIndexedPrerollReader
    {
        public ContainerSeekPoint GetPrerollStart(int streamIndex, long outputPacketIndex, int minimumPrecedingPackets,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this); cancellationToken.ThrowIfCancellationRequested();
                long count = index.GetPacketCount(streamIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(outputPacketIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(outputPacketIndex, count);
                ArgumentOutOfRangeException.ThrowIfNegative(minimumPrecedingPackets);
                long start = Math.Max(0, outputPacketIndex - minimumPrecedingPackets);
                ContainerPacketTiming timing = packets.GetPacketTiming(streamIndex, start, cancellationToken);
                MediaTimestamp pts = timing.PresentationTime ?? throw new NotSupportedException("The Matroska preroll packet has no indexed presentation timestamp.");
                return packets.FindSeekPoint(streamIndex, pts, start + 1, cancellationToken)
                    ?? throw new NotSupportedException("No Matroska restart candidate is indexed before the required preroll.");
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
