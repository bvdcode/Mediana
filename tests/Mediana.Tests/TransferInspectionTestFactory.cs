// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class TransferInspectionTestFactory(Action inspect) : IVideoDecoderFactory, IRandomAccessDecoderFactory, IRandomAccessInspector
    {
        public FixtureDecoderFactory Inner { get; } = new();
        public string ImplementationId => Inner.ImplementationId;
        public string CodecId => Inner.CodecId;
        public string? GetUnsupportedReason(EncodedStreamInfo stream) => Inner.GetUnsupportedReason(stream);

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            IMediaCodec<MediaPacket, VideoFrame> decoder = Inner.Create(stream, allocator);
            using WritableMediaBuffer writable = allocator.Allocate(13);
            using MediaBuffer storage = writable.Publish();
            using MediaPacket packet = new(stream.StreamIndex, storage);
            decoder.TrySend(packet);
            return decoder;
        }

        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default) => true;

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            inspect();
            return this;
        }
    }
}
