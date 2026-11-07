// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Decodes complete independently coded JPEG pictures using the managed JPEG component decoder.
    /// Output is straight RGBA8 at the packet's presentation time; container geometry and color labels take precedence.
    /// Image presentation metadata does not rotate, crop or retime video pictures.</summary>
    public class MjpegDecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        private readonly JpegDecodeOptions options;
        private readonly Action<string>? warningHandler;

        public MjpegDecoderFactory(JpegDecodeOptions? options = null, Action<string>? warningHandler = null)
        {
            this.options = options ?? new();
            this.options.Validate();
            this.warningHandler = warningHandler;
        }

        public string ImplementationId => "mediana.mjpeg";
        public string CodecId => "mjpeg";

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId) { return "The stream is not Motion JPEG video."; }
            return StreamFormats.Mjpeg.GetUnsupportedReason(stream.Format);
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new MjpegDecoder(stream, allocator, options, warningHandler);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason) { throw new NotSupportedException(reason); }
            return new MjpegRandomAccessInspector(stream.StreamIndex, options);
        }
    }
}
