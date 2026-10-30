// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    /// <summary>Decodes mono/stereo AAC-LC, one raw_data_block per packet, with 1024 samples per frame.
    /// Outputs native-endian interleaved float32 PCM. Priming, padding and container edit trimming are not applied.</summary>
    public class AacDecoderFactory : IAudioDecoderFactory
    {
        private readonly int maxAccessUnitBytes;

        public AacDecoderFactory(int maxAccessUnitBytes = 8192)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAccessUnitBytes);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxAccessUnitBytes, int.MaxValue / 8);
            this.maxAccessUnitBytes = maxAccessUnitBytes;
        }

        public string ImplementationId => "mediana.aac";
        public string CodecId => AacConfiguration.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                return "The stream is not AAC audio.";
            }
            string? formatReason = StreamFormats.Aac.GetUnsupportedReason(stream.Format);
            if (formatReason is not null)
            {
                return formatReason;
            }
            AacConfiguration? configuration = AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out string? reason);
            if (configuration is null)
            {
                return reason;
            }
            if (configuration.ChannelConfiguration is not (1 or 2))
            {
                return "Only the standard mono and stereo AAC channel configurations are supported.";
            }
            return configuration.FrequencyIndex == 12 ? "The 7350 Hz AAC band tables are not supported." : null;
        }

        /// <summary>Calls must not overlap. Failure or cancellation requires reset or disposal.
        /// Reset clears overlap and noise state; continuous output requires decoding the preceding access units.</summary>
        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            AacConfiguration configuration = AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out reason)
                ?? throw new NotSupportedException(reason);
            return new AacDecoder(stream.StreamIndex, configuration, allocator, maxAccessUnitBytes);
        }
    }
}
