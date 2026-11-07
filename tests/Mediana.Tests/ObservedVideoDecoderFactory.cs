// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class ObservedVideoDecoderFactory(IVideoDecoderFactory inner) : IVideoDecoderFactory
    {
        public string ImplementationId => inner.ImplementationId;
        public string CodecId => inner.CodecId;
        public MediaBufferAllocator? Allocator { get; private set; }
        public string? GetUnsupportedReason(EncodedStreamInfo stream) => inner.GetUnsupportedReason(stream);
        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            Allocator = allocator;
            return inner.Create(stream, allocator);
        }
    }
}
