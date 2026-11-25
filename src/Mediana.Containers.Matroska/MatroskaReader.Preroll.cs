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
                ulong preroll = Tracks[streamIndex].Timing.SeekPreRollNanoseconds;
                if (preroll != 0 && start > 0)
                {
                    TimeBase nanoseconds = MatroskaTrackClock.TimeBase;
                    MediaTimestamp output = GetPrerollTimestamp(streamIndex, outputPacketIndex, cancellationToken);
                    Int128 cutoff = (Int128)output.Rescale(nanoseconds, TimestampRounding.NearestEven).Value - preroll;
                    if (cutoff < long.MinValue)
                    {
                        start = 0;
                    }
                    else
                    {
                        MediaTimestamp target = new((long)cutoff, nanoseconds);
                        while (start > 0 && GetPrerollTimestamp(streamIndex, start, cancellationToken).CompareTo(target) > 0)
                        {
                            start--;
                        }
                    }
                }
                MediaTimestamp pts = GetPrerollTimestamp(streamIndex, start, cancellationToken);
                return packets.FindSeekPoint(streamIndex, pts, start + 1, cancellationToken)
                    ?? throw new NotSupportedException("No Matroska restart candidate is indexed before the required preroll.");
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private MediaTimestamp GetPrerollTimestamp(int streamIndex, long packetIndex, CancellationToken token) =>
            packets.GetPacketTiming(streamIndex, packetIndex, token).PresentationTime
            ?? throw new NotSupportedException("The Matroska preroll packet has no indexed presentation timestamp.");
    }
}
