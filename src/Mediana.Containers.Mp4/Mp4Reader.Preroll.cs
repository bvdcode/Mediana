// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    public partial class Mp4Reader
    {
        public ContainerSeekPoint GetPrerollStart(int streamIndex, long outputPacketIndex, int minimumPrecedingPackets,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ValidateIndexedStream(streamIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(minimumPrecedingPackets);
                ArgumentOutOfRangeException.ThrowIfNegative(outputPacketIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(outputPacketIndex, Tracks[streamIndex].SampleCount);
                cancellationToken.ThrowIfCancellationRequested();
                int output = (int)outputPacketIndex;
                int preceding = minimumPrecedingPackets;
                bool requiresSync = false;
                foreach (Mp4SampleRecoveryGroup group in Tracks[streamIndex].RecoveryGroups)
                {
                    if (group.GetDistance(output) is not short distance)
                    {
                        continue;
                    }
                    if (distance > 0)
                    {
                        throw new NotSupportedException("Seeking with forward MP4 roll recovery is not supported.");
                    }
                    preceding = Math.Max(preceding, -(int)distance);
                    switch (group.Kind)
                    {
                        case Mp4RecoveryKind.Roll:
                            break;
                        case Mp4RecoveryKind.PreRoll:
                            requiresSync = true;
                            break;
                        default:
                            throw new InvalidDataException("The MP4 recovery group kind is invalid.");
                    }
                }
                int start = Math.Max(0, output - preceding);
                if (requiresSync && start != 0)
                {
                    start = Mp4SyncSampleLookup.AtOrBefore(sampleTables[streamIndex].SyncSamples, start);
                    if (start < 0)
                    {
                        throw new NotSupportedException("No sync packet is available before the MP4 preroll position.");
                    }
                }
                Mp4SampleLocation location = Locate(streamIndex, start, cancellationToken);
                return new ContainerSeekPoint(streamIndex, start, timelines[streamIndex].Map(location.PresentationTime));
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
