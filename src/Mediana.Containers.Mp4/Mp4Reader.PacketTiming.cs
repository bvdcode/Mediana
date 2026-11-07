// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    public partial class Mp4Reader
    {
        public ContainerPacketTiming GetPacketTiming(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(packetIndex, int.MaxValue);
            Mp4SampleInfo sample = GetSampleInfo(streamIndex, (int)packetIndex, cancellationToken);
            return new(sample.PresentationTime, sample.DecodeTime, sample.Duration, sample.IsSync);
        }
    }
}
