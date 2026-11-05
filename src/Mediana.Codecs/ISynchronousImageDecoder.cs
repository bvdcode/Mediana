// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs
{
    /// <summary>Optional in-memory synchronous decoding. Asynchronous-only implementations remain valid image decoders.</summary>
    public interface ISynchronousImageDecoder : IImageDecoder
    {
        VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default);
    }
}
