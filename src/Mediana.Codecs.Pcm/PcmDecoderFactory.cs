// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    /// <summary>Decodes little-endian PCM with a WAVEFORMATEX or WAVEFORMATEXTENSIBLE configuration.</summary>
    public class PcmDecoderFactory : IAudioDecoderFactory, IRandomAccessDecoderFactory
    {
        private readonly int maxSamplesPerFrame;

        public PcmDecoderFactory(int maxSamplesPerFrame = 65536)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamplesPerFrame);
            this.maxSamplesPerFrame = maxSamplesPerFrame;
        }

        public virtual string ImplementationId => "mediana.pcm";
        public virtual string CodecId => PcmConfiguration.CodecId;
        protected virtual EncodedStreamFormat ExpectedFormat => StreamFormats.WavePcm;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                return "The stream is not PCM audio.";
            }
            string? formatReason = ExpectedFormat.GetUnsupportedReason(stream.Format);
            if (formatReason is not null)
            {
                return formatReason;
            }
            ReadConfiguration(stream.Configuration.Span, out string? reason);
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
            PcmConfiguration configuration = ReadConfiguration(stream.Configuration.Span, out reason)
                ?? throw new NotSupportedException(reason);
            return new PcmDecoder(stream.StreamIndex, configuration, allocator, maxSamplesPerFrame);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null) { throw new NotSupportedException(reason); }
            PcmConfiguration configuration = ReadConfiguration(stream.Configuration.Span, out reason)
                ?? throw new NotSupportedException(reason);
            return new PcmRandomAccessInspector(stream.StreamIndex, configuration.BlockAlignment);
        }

        protected virtual PcmConfiguration? ReadConfiguration(ReadOnlySpan<byte> bytes, out string? unsupportedReason) =>
            PcmConfiguration.ReadWaveFormat(bytes, out unsupportedReason);
    }
}
