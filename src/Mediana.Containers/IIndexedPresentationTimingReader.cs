// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>Optional presentation intervals for indexed packets representing complete presentation units.
    /// Raw packet durations remain the container's original intervals. Decode consumers may use this
    /// duration with the same payload and timestamps. Null means the presentation interval is unknown.
    /// Queries preserve packet cursors and do not read encoded media; owned metadata may be cached.</summary>
    public interface IIndexedPresentationTimingReader : IIndexedContainerReader
    {
        MediaDuration? GetPresentationDuration(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default);
    }
}
