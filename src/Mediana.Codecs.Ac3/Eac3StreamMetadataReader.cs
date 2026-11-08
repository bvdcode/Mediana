// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    /// <summary>Reads the main independent E-AC-3 presentation, including declared dependent channel locations.</summary>
    public class Eac3StreamMetadataReader : Ac3StreamMetadataReader
    {
        public override string ImplementationId => "mediana.eac3.metadata";
        public override string CodecId => Ac3FrameHeader.EnhancedCodecId;
        protected override EncodedStreamFormat RawFormat => StreamFormats.Eac3;
        protected override string ProfileName => "E-AC-3";
        protected override bool IsRawFormat(EncodedStreamFormat format) => format == StreamFormats.Eac3 || format == StreamFormats.Eac3Syncframe;

        /// <summary>An independent-frame prefix alone cannot establish the rendered dependent channel composition.</summary>
        public override StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            ReadPrefixHeader(stream, prefix);
            return null;
        }

        public override StreamMetadata? Read(EncodedStreamInfo stream)
        {
            RequireStream(stream);
            if (IsRawFormat(stream.Format)) { return null; }
            StreamFormats.Eac3Mp4.Require(stream.Format);
            Eac3SubstreamConfiguration configuration = Eac3SpecificConfiguration.Read(stream.Configuration.Span).Substreams[0];
            return new AudioStreamMetadata(configuration.SampleRate, configuration.ChannelCount,
                channelOrder: configuration.DependentSubstreamCount == 0 ?
                    Ac3ChannelLayout.GetChannelOrder(configuration.ChannelMode, configuration.HasLfe) : null)
            { CodecProfile = new(ProfileName) };
        }
    }
}
