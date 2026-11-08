// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    /// <summary>Managed AC-3 decoder accepting one complete, CRC-protected syncframe per packet.
    /// Output preserves coded channel order in native-endian interleaved float32 PCM.</summary>
    public class Ac3DecoderFactory(bool applyDynamicRangeCompression = true) : IAudioDecoderFactory
    {
        public virtual string CodecId => Ac3FrameHeader.CodecId;
        public virtual string ImplementationId => "mediana.ac3";
        protected virtual bool Enhanced => false;
        protected virtual EncodedStreamFormat ExpectedFormat => StreamFormats.Ac3;
        protected virtual EncodedStreamFormat ExpectedMp4Format => StreamFormats.Ac3Mp4;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId) { return "The stream is not the expected AC-3 audio codec."; }
            if (stream.Format == ExpectedFormat)
            {
                return stream.Configuration.IsEmpty ? null : "AC-3 syncframes are self-describing and require an empty external configuration.";
            }
            string? reason = ExpectedMp4Format.GetUnsupportedReason(stream.Format);
            if (reason is not null) { return reason; }
            if (!Enhanced) { Ac3SpecificConfiguration.Read(stream.Configuration.Span); return null; }
            Eac3SpecificConfiguration configuration = Eac3SpecificConfiguration.Read(stream.Configuration.Span);
            if (configuration.Substreams.Count != 1) { return "Multiple independent E-AC-3 substreams require presentation selection and are not supported."; }
            Eac3SubstreamConfiguration substream = configuration.Substreams[0];
            if (substream.DependentSubstreamCount > 8) { return "An E-AC-3 programme exceeds eight dependent substreams."; }
            if (substream.AssociatedService) { return "Associated E-AC-3 services require a main presentation for mixing and are not supported."; }
            return null;
        }

        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null) { throw new NotSupportedException(reason); }
            Ac3DecoderExpectation? expectation = null;
            if (stream.Format == ExpectedMp4Format)
            {
                if (Enhanced)
                {
                    Eac3SubstreamConfiguration config = Eac3SpecificConfiguration.Read(stream.Configuration.Span).Substreams[0];
                    expectation = new(config.SampleRate, config.BitstreamId, config.ChannelMode, config.HasLfe,
                        config.DependentSubstreamCount, config.DependentChannelLocations);
                }
                else
                {
                    Ac3SpecificConfiguration config = Ac3SpecificConfiguration.Read(stream.Configuration.Span);
                    expectation = new(config.SampleRate, config.BitstreamId, config.ChannelMode, config.HasLfe);
                }
            }
            return new Ac3Decoder(stream.StreamIndex, Enhanced, allocator, applyDynamicRangeCompression, expectation);
        }
    }
}
