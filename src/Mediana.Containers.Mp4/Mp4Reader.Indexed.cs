// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    public partial class Mp4Reader
    {
        public long GetPacketCount(int streamIndex)
        {
            Enter();
            try
            {
                ValidateIndexedStream(streamIndex);
                return Tracks[streamIndex].SampleCount;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public ContainerSeekPoint? FindSeekPoint(int streamIndex, MediaTimestamp target, long beforePacketIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ValidateIndexedStream(streamIndex);
                target.TimeBase.Validate();
                ArgumentOutOfRangeException.ThrowIfNegative(beforePacketIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(beforePacketIndex, Tracks[streamIndex].SampleCount);
                cancellationToken.ThrowIfCancellationRequested();
                int? sample = lookups[streamIndex].FindSeekSample(timelines[streamIndex].UnmapFloor(target), (int)beforePacketIndex, cancellationToken);
                if (sample is not int index)
                {
                    return null;
                }
                Mp4SampleLocation location = Locate(streamIndex, index, cancellationToken);
                return new ContainerSeekPoint(streamIndex, index, timelines[streamIndex].Map(location.PresentationTime));
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        ValueTask<MediaPacket> IIndexedContainerReader.ReadPacketAtAsync(int streamIndex, long packetIndex,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(packetIndex, int.MaxValue);
            return ReadPacketAtAsync(streamIndex, (int)packetIndex, cancellationToken);
        }

        private void ValidateIndexedStream(int streamIndex)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, Tracks.Count);
        }
    }
}
