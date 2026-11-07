// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Ogg
{
    internal class OggOpusHeaders : OggAudioHeaders
    {
        private readonly OpusConfiguration configuration;
        private readonly byte[] header;

        public OggOpusHeaders(ReadOnlySpan<byte> first)
        {
            configuration = OpusConfiguration.Read(first);
            header = first.ToArray();
        }

        public override string CodecId => OpusConfiguration.CodecId;
        public override EncodedStreamFormat Format => StreamFormats.Opus;
        public override int SampleRate => OpusConfiguration.ClockRate;
        public override int ChannelCount => configuration.ChannelCount;
        public override int HeaderCount => 2;
        public override int PreSkip => configuration.PreSkipSamples;
        public override int SeekPreroll => OpusConfiguration.SeekPrerollSamples;
        public override int MinimumSeekPackets => 0;
        public override bool FirstPacketIsPreroll => false;
        public override bool AllowNegativeOrigin => false;
        public override int InitialLead(int firstPacketSamples) => PreSkip;
        public override ReadOnlyMemory<byte> Configuration => header;

        public override void ReadHeader(OggPacket packet, CancellationToken token)
        {
            if (packet.PacketIndex != 1 || !packet.Data.Span.StartsWith("OpusTags"u8))
            {
                throw new InvalidDataException("The Opus comment header is absent or out of order.");
            }
            Comments = VorbisComments.Read(packet.Data.Span[8..], out _, cancellationToken: token);
        }

        public override int ReadSamples(ReadOnlySpan<byte> packet)
            => (configuration.StreamCount == 1 ? OpusPacketInfo.Read(packet) : OpusPacketInfo.ReadTiming(packet)).SampleCount;

        public override void AddMetadata(List<MediaProperty> properties)
        {
            properties.AddRange([new(CodecId, "Version", configuration.Version),
                new(CodecId, "InputSampleRate", configuration.InputSampleRate),
                new(CodecId, "OutputGainQ8", configuration.OutputGainQ8),
                new(CodecId, "MappingFamily", configuration.MappingFamily),
                new(CodecId, "StreamCount", configuration.StreamCount),
                new(CodecId, "CoupledStreamCount", configuration.CoupledStreamCount),
                new(CodecId, "ChannelMapping", MediaPropertyValue.FromBytes(configuration.ChannelMapping.Span))]);
        }
    }
}
