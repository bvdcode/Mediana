// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Execution
{
    /// <summary>A pull-driven stream with immutable metadata. Returned packets transfer ownership;
    /// source disposal does not dispose borrowed inputs.</summary>
    public interface IEncodedPacketSource : IAsyncDisposable
    {
        EncodedStreamInfo Stream { get; }
        ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default);
    }
}
