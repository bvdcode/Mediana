// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    /// <summary>Decodes AAC-LC channel configurations 1–7 and mono/stereo HE-AAC, one raw_data_block per packet.
    /// Outputs native-endian interleaved float32 PCM. Priming, padding and container edit trimming are not applied.</summary>
    public class AacDecoderFactory : IAudioDecoderFactory, IRandomAccessDecoderFactory
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
            if (configuration.ParametricStereo == true)
            {
                return "AAC parametric stereo decoding is not supported.";
            }
            if (configuration.SpectralBandReplication == true)
            {
                if (configuration.ChannelCount > 2)
                {
                    return "SBR decoding requires mono or stereo audio.";
                }
            }
            return configuration.FrequencyIndex == 12 ? "The 7350 Hz AAC band tables are not supported." : null;
        }

        /// <summary>Calls must not overlap. Failure or cancellation requires reset or disposal.
        /// Reset clears overlap and noise state; continuous output requires decoding the preceding access units.</summary>
        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            AacConfiguration configuration = RequireConfiguration(stream);
            return configuration.SpectralBandReplication == true ?
                new AacSbrDecoder(stream.StreamIndex, configuration, allocator, maxAccessUnitBytes) :
                new AacDecoder(stream.StreamIndex, configuration, allocator, maxAccessUnitBytes);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            AacConfiguration configuration = RequireConfiguration(stream);
            return new AacRandomAccessInspector(stream.StreamIndex, configuration, maxAccessUnitBytes);
        }

        private AacConfiguration RequireConfiguration(EncodedStreamInfo stream)
        {
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            return AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out reason)
                ?? throw new NotSupportedException(reason);
        }
    }
}
