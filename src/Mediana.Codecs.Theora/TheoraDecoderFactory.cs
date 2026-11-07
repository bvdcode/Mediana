// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Theora
{
    /// <summary>Managed Theora 3.2 video decoding into pooled YUV 4:2:0, 4:2:2 or 4:4:4.
    /// Inputs and allocator are borrowed. Output frames retain independent leases. Reset requires a keyframe.</summary>
    public class TheoraDecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.theora";
        public string CodecId => TheoraIdentification.CodecId;
        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId) { return "The stream is not Theora video."; }
            return StreamFormats.Theora.GetUnsupportedReason(stream.Format);
        }
        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new TheoraDecoder(stream, TheoraConfiguration.Read(stream.Configuration), allocator);
        }
        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            _ = TheoraConfiguration.Read(stream.Configuration);
            return new TheoraRandomAccessInspector(stream.StreamIndex);
        }
    }
}
