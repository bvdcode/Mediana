// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    /// <summary>Managed MPEG-4 Visual I/P/B decoding into native 8-bit YUV 4:2:0 frames.
    /// Each packet contains one complete VOP and optional preceding headers. Frames have independent leases and display order.</summary>
    public class Mpeg4DecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.mpeg4-visual";
        public string CodecId => Mpeg4VisualConfiguration.CodecId;
        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId) { return "The stream is not MPEG-4 Visual video."; }
            return StreamFormats.Mpeg4Visual.GetUnsupportedReason(stream.Format);
        }
        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new Mpeg4Decoder(stream, allocator);
        }
        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new Mpeg4RandomAccessInspector(stream);
        }
    }
}
