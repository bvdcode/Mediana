// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.IO
{
    /// <summary>A sequential source. A zero read for a nonempty destination means EOF.
    /// Instances support one reader at a time. Length, when known, is measured from the initial position.</summary>
    public interface IByteSource : IAsyncDisposable
    {
        long? Length { get; }
        ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default);
    }
}
