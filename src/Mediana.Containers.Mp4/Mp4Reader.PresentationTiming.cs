// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    public partial class Mp4Reader
    {
        /// <summary>Resolves the sample's presentation interval without reading encoded bytes.
        /// Reordered tracks lazily sort bounded sample metadata; duplicate composition times have unknown intervals.
        /// The last displayed sample keeps its decode duration. Raw packet/sample durations remain unchanged.</summary>
        public MediaDuration? GetPresentationDuration(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ValidateIndexedStream(streamIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(packetIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(packetIndex, Tracks[streamIndex].SampleCount);
                cancellationToken.ThrowIfCancellationRequested();
                Mp4SampleLocation sample = Locate(streamIndex, (int)packetIndex, cancellationToken);
                Mp4SampleTable table = sampleTables[streamIndex];
                if (table.Composition is null || table.Composition.Count <= 1)
                {
                    return timelines[streamIndex].MapDuration(sample.Duration);
                }
                Mp4PresentationTiming timing = presentationTimings[streamIndex]
                    ??= Mp4PresentationTiming.Create(table, allocator, cancellationToken);
                long? duration = timing.GetDuration((int)packetIndex, sample.Duration);
                return duration is long known ? timelines[streamIndex].MapDuration(known) : null;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
