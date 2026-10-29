// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Containers
{
    /// <summary>Owns internal buffers, but borrows its byte source. Each returned packet belongs to the caller.</summary>
    public interface IContainerReader : IAsyncDisposable
    {
        IReadOnlyList<EncodedStreamInfo> Streams { get; }
        ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default);
    }
}
