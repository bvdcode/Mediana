// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Metadata;

namespace Mediana.Codecs.Pcm
{
    /// <summary>Reads PCM sample precision and channel order from WAVE format configuration.</summary>
    public class PcmStreamMetadataReader : IStreamMetadataReader
    {
        public virtual string ImplementationId => "mediana.pcm.metadata";
        public virtual string CodecId => PcmConfiguration.CodecId;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                throw new ArgumentException("The stream is not the expected PCM encoding.", nameof(stream));
            }
            PcmConfiguration? configuration = ReadConfiguration(stream.Configuration.Span);
            if (configuration is null)
            {
                return null;
            }
            return new AudioStreamMetadata(configuration.Format.SampleRate, configuration.Format.Channels.Count,
                configuration.ValidBitsPerSample, configuration.Format.Channels);
        }

        protected virtual PcmConfiguration? ReadConfiguration(ReadOnlySpan<byte> bytes) =>
            PcmConfiguration.ReadWaveFormat(bytes, out _);
    }
}
