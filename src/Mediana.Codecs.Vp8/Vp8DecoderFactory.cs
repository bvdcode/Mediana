// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Vp8
{
    /// <summary>Decodes complete VP8 coded frames into shared planar YUV420 storage.
    /// Hidden pictures update reference state without producing output. A keyframe is required after reset.</summary>
    public class Vp8DecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.vp8";
        public string CodecId => "vp8";

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId) { return "The stream is not VP8 video."; }
            return StreamFormats.Vp8.GetUnsupportedReason(stream.Format);
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new Vp8Decoder(stream, allocator);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new Vp8RandomAccessInspector(stream.StreamIndex);
        }
    }
}
