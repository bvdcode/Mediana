// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    /// <summary>Reads AC-3 sampling parameters from dac3 or an opening syncframe prefix without decoding audio.</summary>
    public class Ac3StreamMetadataReader : IPacketPrefixMetadataReader
    {
        public virtual string ImplementationId => "mediana.ac3.metadata";
        public virtual string CodecId => Ac3FrameHeader.CodecId;
        protected virtual EncodedStreamFormat RawFormat => StreamFormats.Ac3;
        protected virtual string ProfileName => "AC-3";
        protected virtual bool IsRawFormat(EncodedStreamFormat format) => format == RawFormat;
        public int PacketProbeLength => Ac3FrameHeader.HeaderBytes;

        public virtual StreamMetadata? Read(EncodedStreamInfo stream)
        {
            RequireStream(stream);
            if (IsRawFormat(stream.Format)) { return null; }
            StreamFormats.Ac3Mp4.Require(stream.Format);
            Ac3SpecificConfiguration configuration = Ac3SpecificConfiguration.Read(stream.Configuration.Span);
            return new AudioStreamMetadata(configuration.SampleRate, configuration.ChannelCount,
                channelOrder: configuration.GetChannelOrder()) { CodecProfile = new(ProfileName) };
        }

        public virtual StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            Ac3FrameHeader header = ReadPrefixHeader(stream, prefix);
            return new AudioStreamMetadata(header.SampleRate, header.ChannelCount, channelOrder: header.GetChannelOrder())
            { CodecProfile = new(ProfileName) };
        }

        protected Ac3FrameHeader ReadPrefixHeader(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            RequireStream(stream);
            if (!IsRawFormat(stream.Format)) { RawFormat.Require(stream.Format); }
            Ac3FrameHeader header = Ac3FrameHeader.Read(prefix);
            if (header.IsEnhanced != (RawFormat == StreamFormats.Eac3))
            {
                throw new InvalidDataException("The opening syncframe differs from the declared audio codec.");
            }
            return header;
        }

        protected void RequireStream(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            { throw new ArgumentException("The stream is not the expected AC-3 audio codec.", nameof(stream)); }
            if (IsRawFormat(stream.Format) && !stream.Configuration.IsEmpty)
            { throw new InvalidDataException("Self-describing AC-3 syncframes require an empty external configuration."); }
        }
    }
}
