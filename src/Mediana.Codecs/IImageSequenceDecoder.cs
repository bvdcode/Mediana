// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs
{
    /// <summary>Decodes animation frames in presentation order while reusing parsed input.
    /// Encoded bytes remain borrowed throughout enumeration. Each yielded frame transfers independent ownership to the caller.</summary>
    public interface IImageSequenceDecoder : IImageDecoder
    {
        IAsyncEnumerable<VideoFrame> DecodeFramesAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default);
    }
}
