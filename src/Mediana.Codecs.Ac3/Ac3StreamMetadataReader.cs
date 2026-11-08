// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    /// <summary>Reads declared AC-3 sampling parameters from dac3 without decoding audio.</summary>
    public class Ac3StreamMetadataReader : IStreamMetadataReader
    {
        public virtual string ImplementationId => "mediana.ac3.metadata";
        public virtual string CodecId => Ac3FrameHeader.CodecId;
        protected virtual EncodedStreamFormat RawFormat => StreamFormats.Ac3;

        public virtual StreamMetadata? Read(EncodedStreamInfo stream)
        {
            RequireStream(stream);
            if (stream.Format == RawFormat) { return null; }
            StreamFormats.Ac3Mp4.Require(stream.Format);
            Ac3SpecificConfiguration configuration = Ac3SpecificConfiguration.Read(stream.Configuration.Span);
            return new AudioStreamMetadata(configuration.SampleRate, configuration.ChannelCount,
                channelOrder: configuration.GetChannelOrder()) { CodecProfile = new("AC-3") };
        }

        protected void RequireStream(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            { throw new ArgumentException("The stream is not the expected AC-3 audio codec.", nameof(stream)); }
            if (stream.Format == RawFormat && !stream.Configuration.IsEmpty)
            { throw new InvalidDataException("Self-describing AC-3 syncframes require an empty external configuration."); }
        }
    }
}
