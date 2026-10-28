using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    /// <summary>Decodes AVC length-prefixed packets containing a complete single-slice I or P picture.
    /// Supports progressive 8-bit YUV 4:2:0 CAVLC/CABAC I/P pictures with default scaling.
    /// CAVLC supports 4x4 and 8x8 transforms. CABAC 8x8 and B pictures are not supported.</summary>
    public class H264DecoderFactory : IVideoDecoderFactory
    {
        private readonly H264DecoderOptions options;

        public H264DecoderFactory(H264DecoderOptions? options = null)
        {
            this.options = options ?? new H264DecoderOptions();
            this.options.Validate();
        }

        public string ImplementationId => "mediana.h264";
        public string CodecId => "h264";

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                return "The stream is not H.264 video.";
            }
            return H264Support.GetUnsupportedReason(H264Configuration.Parse(stream.Configuration.Span, options));
        }

        /// <summary>Outputs owned cropped YUV frames with the packet presentation time and duration.
        /// Calls must not overlap. Cancellation or failure requires Reset or disposal.
        /// Reset restores the original AVC configuration and requires a new IDR picture.</summary>
        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(allocator);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                throw new NotSupportedException("The stream is not H.264 video.");
            }
            H264Configuration configuration = H264Configuration.Parse(stream.Configuration.Span, options);
            string? reason = H264Support.GetUnsupportedReason(configuration);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            return new H264Decoder(stream.StreamIndex, configuration, allocator, options);
        }
    }
}
