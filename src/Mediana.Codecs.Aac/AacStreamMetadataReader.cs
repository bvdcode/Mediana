// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Metadata;

namespace Mediana.Codecs.Aac
{
    /// <summary>Reads indexed AAC-LC sample rate and channel count from AudioSpecificConfig.
    /// Reading parameters does not require decoder support for the channel configuration.</summary>
    public class AacStreamMetadataReader : IStreamMetadataReader
    {
        public string ImplementationId => "mediana.aac.metadata";
        public string CodecId => AacConfiguration.CodecId;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                throw new ArgumentException("The stream is not AAC audio.", nameof(stream));
            }
            AacConfiguration? configuration = AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out _);
            if (configuration is null)
            {
                return null;
            }
            return new AudioStreamMetadata(configuration.SampleRate, configuration.ChannelCount);
        }
    }
}
