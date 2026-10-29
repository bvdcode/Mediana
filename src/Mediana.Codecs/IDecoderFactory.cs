// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Codecs
{
    public interface IDecoderFactory<TFrame> where TFrame : class, IDisposable
    {
        string ImplementationId { get; }
        string CodecId { get; }
        /// <summary>Returns null only for supported configurations; otherwise returns the rejection reason.</summary>
        string? GetUnsupportedReason(EncodedStreamInfo stream);
        IMediaCodec<MediaPacket, TFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator);
    }
}
