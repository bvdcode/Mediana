using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Pcm
{
    /// <summary>Decodes little-endian PCM with a WAVEFORMATEX or WAVEFORMATEXTENSIBLE configuration.</summary>
    public class PcmDecoderFactory : IAudioDecoderFactory
    {
        private readonly int maxSamplesPerFrame;

        public PcmDecoderFactory(int maxSamplesPerFrame = 65536)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamplesPerFrame);
            this.maxSamplesPerFrame = maxSamplesPerFrame;
        }

        public string ImplementationId => "mediana.pcm";
        public string CodecId => PcmConfiguration.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                return "The stream is not PCM audio.";
            }
            PcmConfiguration.ReadWaveFormat(stream.Configuration.Span, out string? reason);
            return reason;
        }

        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            PcmConfiguration configuration = PcmConfiguration.ReadWaveFormat(stream.Configuration.Span, out reason)
                ?? throw new NotSupportedException(reason);
            return new PcmDecoder(stream.StreamIndex, configuration, allocator, maxSamplesPerFrame);
        }
    }
}
