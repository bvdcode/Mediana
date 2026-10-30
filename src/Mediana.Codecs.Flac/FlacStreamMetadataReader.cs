// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Metadata;

namespace Mediana.Codecs.Flac
{
    /// <summary>Reads sample parameters from native FLAC STREAMINFO without interpreting channel-layout comments.</summary>
    public class FlacStreamMetadataReader : IStreamMetadataReader
    {
        public string ImplementationId => "mediana.flac.metadata";
        public string CodecId => FlacConfiguration.CodecId;

        public StreamMetadata Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                throw new ArgumentException("The stream is not FLAC audio.", nameof(stream));
            }
            FlacConfiguration configuration = FlacConfiguration.ReadStreamInfo(stream.Configuration.Span);
            return new AudioStreamMetadata(configuration.SampleRate, configuration.ChannelCount, configuration.BitsPerSample);
        }
    }
}
