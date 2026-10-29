// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class FixtureDecoderFactory(string id = "fixture.decoder", string? rejection = null) : IVideoDecoderFactory
    {
        public string ImplementationId => id;
        public string CodecId => "fixture.rgba";
        public DelayedDecoder? LastDecoder { get; private set; }
        public MediaBufferAllocator? Allocator { get; private set; }
        public string? GetUnsupportedReason(EncodedStreamInfo stream) => rejection;

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            Allocator = allocator;
            LastDecoder = new DelayedDecoder(allocator);
            return LastDecoder;
        }
    }
}
