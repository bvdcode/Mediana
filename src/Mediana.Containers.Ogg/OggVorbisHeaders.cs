// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Ogg
{
    internal class OggVorbisHeaders : OggAudioHeaders
    {
        private readonly VorbisIdentification identification;
        private readonly byte[] first;
        private byte[] comments = [];
        private byte[] configuration = [];
        private VorbisSetupInfo? setup;
        private int previousBlock;

        public OggVorbisHeaders(ReadOnlySpan<byte> first)
        {
            identification = VorbisIdentification.Read(first);
            this.first = first.ToArray();
        }

        public override string CodecId => VorbisIdentification.CodecId;
        public override EncodedStreamFormat Format => StreamFormats.Vorbis;
        public override int SampleRate => identification.SampleRate;
        public override int ChannelCount => identification.ChannelCount;
        public override int HeaderCount => 3;
        public override int PreSkip => 0;
        public override int SeekPreroll => 0;
        public override int MinimumSeekPackets => 1;
        public override bool FirstPacketIsPreroll => true;
        public override bool AllowNegativeOrigin => true;
        public override int InitialLead(int firstPacketSamples) => firstPacketSamples;
        public override ReadOnlyMemory<byte> Configuration => configuration;

        public override void ReadHeader(OggPacket packet, CancellationToken token)
        {
            switch (packet.PacketIndex)
            {
                case 1:
                    if (packet.Data.Length < 7 || packet.Data.Span[0] != 3 || !packet.Data.Span.Slice(1, 6).SequenceEqual("vorbis"u8))
                    {
                        throw new InvalidDataException("The Vorbis comment header is absent or out of order.");
                    }
                    Comments = VorbisComments.Read(packet.Data.Span[7..], out int consumed, cancellationToken: token);
                    if (consumed >= packet.Data.Length - 7 || (packet.Data.Span[7 + consumed] & 1) == 0)
                    {
                        throw new InvalidDataException("The Vorbis comment framing bit is absent.");
                    }
                    comments = packet.Data.ToArray();
                    break;
                case 2:
                    setup = VorbisSetupInfo.Read(identification, packet.Data.Span, token);
                    configuration = XiphLacedHeaders.Write(first, comments, packet.Data.Span);
                    comments = [];
                    break;
                default:
                    throw new InvalidDataException("The Vorbis headers are out of order.");
            }
        }

        public override int ReadSamples(ReadOnlySpan<byte> packet)
        {
            int block = setup!.GetBlockSize(packet);
            int duration = previousBlock == 0 ? block / 2 : (previousBlock + block) / 4;
            previousBlock = block;
            return duration;
        }

        public override void AddMetadata(List<MediaProperty> properties)
        {
            properties.AddRange([new(CodecId, "SmallBlockSize", identification.SmallBlockSize),
                new(CodecId, "LargeBlockSize", identification.LargeBlockSize),
                new(CodecId, "MaximumBitRate", identification.MaximumBitRate),
                new(CodecId, "NominalBitRate", identification.NominalBitRate),
                new(CodecId, "MinimumBitRate", identification.MinimumBitRate),
                new(CodecId, "ModeCount", setup!.ModeCount)]);
        }

    }
}
