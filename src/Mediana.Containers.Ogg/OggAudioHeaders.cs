// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Ogg
{
    internal abstract class OggAudioHeaders
    {
        public abstract string CodecId { get; }
        public abstract EncodedStreamFormat Format { get; }
        public abstract int SampleRate { get; }
        public abstract int ChannelCount { get; }
        public abstract int HeaderCount { get; }
        public abstract int PreSkip { get; }
        public abstract int SeekPreroll { get; }
        public abstract int MinimumSeekPackets { get; }
        public abstract bool FirstPacketIsPreroll { get; }
        public abstract bool AllowNegativeOrigin { get; }
        public abstract int InitialLead(int firstPacketSamples);
        public abstract ReadOnlyMemory<byte> Configuration { get; }
        public VorbisComments? Comments { get; protected set; }
        public abstract void ReadHeader(OggPacket packet, CancellationToken token);
        public abstract int ReadSamples(ReadOnlySpan<byte> packet);
        public abstract void AddMetadata(List<MediaProperty> properties);

        public static OggAudioHeaders Create(ReadOnlySpan<byte> first)
        {
            if (first.StartsWith("OpusHead"u8)) { return new OggOpusHeaders(first); }
            if (first.Length >= 7 && first[0] == 1 && first.Slice(1, 6).SequenceEqual("vorbis"u8))
            {
                return new OggVorbisHeaders(first);
            }
            throw new NotSupportedException("The Ogg logical stream does not use Opus or Vorbis.");
        }
    }
}
