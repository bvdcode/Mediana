// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Vp9
{
    /// <summary>Decodes complete VP9 access units into pooled native-precision planar video.
    /// An access unit may prefix its visible picture with hidden reference pictures.
    /// Reset requires a new keyframe. Inputs and allocator are borrowed; output frames own independent leases.</summary>
    public class Vp9DecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.vp9";
        public string CodecId => "vp9";
        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId) { return "The stream is not VP9 video."; }
            return StreamFormats.Vp9.GetUnsupportedReason(stream.Format);
        }
        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new Vp9Decoder(stream, allocator);
        }
        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new Vp9RandomAccessInspector(stream.StreamIndex);
        }
    }
}
