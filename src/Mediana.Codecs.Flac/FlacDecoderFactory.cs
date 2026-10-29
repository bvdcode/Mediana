using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Flac
{
    /// <summary>Decodes FLAC frames from arbitrary contiguous encoded chunks, starting at frame zero.
    /// Outputs native-endian interleaved signed32 PCM with the original precision left-aligned.
    /// Checks frame/header CRC and sequential sample counts; STREAMINFO MD5 verification is not performed.</summary>
    public class FlacDecoderFactory : IAudioDecoderFactory
    {
        private readonly int maxFrameBytes;

        public FlacDecoderFactory(int maxFrameBytes = 4 * 1024 * 1024)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameBytes, 16);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFrameBytes, int.MaxValue / 8);
            this.maxFrameBytes = maxFrameBytes;
        }

        public string ImplementationId => "mediana.flac";
        public string CodecId => FlacConfiguration.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                return "The stream is not FLAC audio.";
            }
            FlacConfiguration configuration = FlacConfiguration.ReadStreamInfo(stream.Configuration.Span);
            return configuration.MinimumFrameBytes > maxFrameBytes ? "The FLAC frames exceed the configured byte limit." : null;
        }

        /// <summary>Calls must not overlap. Failure/cancellation requires reset or disposal.
        /// Reset restarts frame/sample numbering at zero. Custom channel masks require container-level handling.</summary>
        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            FlacConfiguration configuration = FlacConfiguration.ReadStreamInfo(stream.Configuration.Span);
            return new FlacDecoder(stream.StreamIndex, configuration, allocator, maxFrameBytes);
        }
    }
}
