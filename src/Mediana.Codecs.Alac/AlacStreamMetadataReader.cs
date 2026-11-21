// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Core.Audio;
using Mediana.Core.Metadata;

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Alac
{
    /// <summary>Reads ALAC sample rate, channel count and source precision without decoding packets.</summary>
    public class AlacStreamMetadataReader : IStreamMetadataReader
    {
        public string ImplementationId => "mediana.alac.metadata";
        public string CodecId => AlacConfiguration.CodecId;

        public StreamMetadata Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                throw new ArgumentException("The stream is not ALAC audio.", nameof(stream));
            }
            StreamFormats.Alac.Require(stream.Format);
            AlacConfiguration configuration = AlacConfiguration.Read(stream.Configuration.Span);
            return new AudioStreamMetadata(configuration.SampleRate, configuration.ChannelCount, configuration.BitDepth,
                configuration.ChannelCount <= 2 || configuration.ChannelLayoutTag is not null ? configuration.GetChannelOrder() : null);
        }
    }
}
