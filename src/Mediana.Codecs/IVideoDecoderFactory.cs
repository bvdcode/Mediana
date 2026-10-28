using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs
{
    public interface IVideoDecoderFactory
    {
        string ImplementationId { get; }
        string CodecId { get; }
        /// <summary>Returns null only for supported configurations; otherwise returns the rejection reason.</summary>
        string? GetUnsupportedReason(EncodedStreamInfo stream);
        IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator);
    }
}
