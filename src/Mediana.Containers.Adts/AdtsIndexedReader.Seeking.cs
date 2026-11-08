// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.Adts
{
    public partial class AdtsIndexedReader
    {
        public long GetPacketCount(int streamIndex)
        {
            Enter();
            try { return RequireStream(streamIndex); }
            finally { Exit(); }
        }

        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                int count = RequireStream(streamIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(beforePacketIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(beforePacketIndex, count);
                target.TimeBase.Validate();
                cancellationToken.ThrowIfCancellationRequested();
                if (beforePacketIndex == 0) { return null; }
                int low = 0, high = (int)beforePacketIndex - 1;
                while (low < high)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int middle = low + (high - low + 1) / 2;
                    if (Timestamp(middle).CompareTo(target) <= 0) { low = middle; }
                    else { high = middle - 1; }
                }
                return new(0, low, Timestamp(low));
            }
            finally { Exit(); }
        }

        public ContainerSeekPoint GetPrerollStart(int streamIndex, long outputPacketIndex, int minimumPrecedingPackets,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                int output = RequirePacket(streamIndex, outputPacketIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(minimumPrecedingPackets);
                cancellationToken.ThrowIfCancellationRequested();
                int start = (int)Math.Max(0L, (long)output - minimumPrecedingPackets);
                return new(0, start, Timestamp(start));
            }
            finally { Exit(); }
        }

        public ContainerPacketTiming GetPacketTiming(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                int index = RequirePacket(streamIndex, packetIndex);
                cancellationToken.ThrowIfCancellationRequested();
                MediaTimestamp timestamp = Timestamp(index);
                return new(timestamp, timestamp, Duration, null);
            }
            finally { Exit(); }
        }

        public MediaDuration? GetPresentationDuration(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                RequirePacket(streamIndex, packetIndex);
                cancellationToken.ThrowIfCancellationRequested();
                return Duration;
            }
            finally { Exit(); }
        }
    }
}
