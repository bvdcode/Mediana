// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    /// <summary>Decodes one WAVE compressed audio encoding to PCM16 and reads its stream metadata from the format chunk.</summary>
    public class WaveCompressedAudioCodec : IAudioDecoderFactory, IStreamMetadataReader
    {
        private readonly WaveCompressedAudioEncoding encoding;
        private readonly EncodedStreamFormat format;
        private readonly int maxSamplesPerFrame;

        public WaveCompressedAudioCodec(WaveCompressedAudioEncoding encoding, int maxSamplesPerFrame = 65536)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamplesPerFrame);
            this.encoding = encoding;
            this.maxSamplesPerFrame = maxSamplesPerFrame;
            CodecId = WaveCompressedAudioConfiguration.GetCodecId(encoding);
            format = WaveCompressedAudioConfiguration.GetStreamFormat(encoding);
        }

        public string ImplementationId => $"mediana.{CodecId}";
        public string CodecId { get; }

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            _ = GetConfiguration(stream, out string? reason);
            return reason;
        }

        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            WaveCompressedAudioConfiguration configuration = GetConfiguration(stream, out string? reason)
                ?? throw new NotSupportedException(reason);
            return new WaveCompressedAudioDecoder(stream.StreamIndex, configuration, allocator, maxSamplesPerFrame);
        }

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            WaveCompressedAudioConfiguration? configuration = GetConfiguration(stream, out _);
            if (configuration is null)
            {
                return null;
            }
            AudioFormat decodedFormat = configuration.DecodedFormat;
            return new AudioStreamMetadata(decodedFormat.SampleRate, decodedFormat.Channels.Count, channelOrder: decodedFormat.Channels);
        }

        private WaveCompressedAudioConfiguration? GetConfiguration(EncodedStreamInfo stream, out string? reason)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                reason = "The stream does not use the expected compressed WAVE encoding.";
                return null;
            }
            reason = format.GetUnsupportedReason(stream.Format);
            if (reason is not null)
            {
                return null;
            }
            WaveCompressedAudioConfiguration? configuration = WaveCompressedAudioConfiguration.ReadWaveFormat(stream.Configuration.Span);
            if (configuration?.Encoding != encoding)
            {
                reason = "The WAVE format tag does not match the stream codec.";
                return null;
            }
            return configuration;
        }
    }
}
