// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>Provides a decode clock for indexed copy when the container stores presentation times only.
    /// Queries preserve packet cursors and do not read encoded payloads.</summary>
    public interface IIndexedDecodeTimingReader : IIndexedContainerReader
    {
        MediaTimestamp? GetDecodeTime(int streamIndex, long packetIndex, CancellationToken cancellationToken = default);
    }
}
