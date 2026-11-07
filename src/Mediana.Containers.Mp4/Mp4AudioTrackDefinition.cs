// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Mp4
{
    /// <summary>Describes AAC-LC access units in fragmented MP4, including their explicit presentation offset.</summary>
    public class Mp4AudioTrackDefinition : Mp4TrackDefinition
    {
        public Mp4AudioTrackDefinition(EncodedStreamInfo stream, uint trackId, long timestampOffset = 0)
            : base(stream, trackId, timestampOffset)
        {
            if (stream.Kind != MediaKind.Audio || stream.CodecId != AacConfiguration.CodecId)
            { throw new NotSupportedException("Fragmented audio writing requires AAC-LC access units."); }
            StreamFormats.Aac.Require(stream.Format);
            Configuration = AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out string? reason)
                ?? throw new NotSupportedException(reason);
            if (Configuration.SampleRate > ushort.MaxValue)
            { throw new NotSupportedException("This audio sample entry requires a sampling frequency at most 65535 Hz."); }
        }

        public AacConfiguration Configuration { get; }
        internal override void WritePresentationSize(Mp4BoxWriter writer) => writer.Zeros(8);
        internal override void WriteMediaHeader(Mp4BoxWriter writer)
        {
            int smhd = writer.Full("smhd");
            writer.Zeros(4);
            writer.End(smhd);
        }

        internal override void WriteSampleEntry(Mp4BoxWriter writer)
        {
            int sample = writer.Begin("mp4a");
            writer.Zeros(6);
            writer.UInt16(1);
            writer.Zeros(8);
            writer.UInt16((ushort)Configuration.ChannelCount);
            writer.UInt16(16);
            writer.Zeros(4);
            writer.UInt32((uint)Configuration.SampleRate << 16);
            int esds = writer.Full("esds");
            int configBytes = Stream.Configuration.Length;
            int decoderBytes = checked(13 + DescriptorBytes(configBytes));
            Descriptor(writer, 3, checked(3 + DescriptorBytes(decoderBytes) + 3));
            writer.UInt16(1);
            writer.Byte(0);
            Descriptor(writer, 4, decoderBytes);
            writer.Byte(0x40);
            writer.Byte(0x15);
            writer.Zeros(11);
            Descriptor(writer, 5, configBytes);
            writer.Bytes(Stream.Configuration.Span);
            Descriptor(writer, 6, 1);
            writer.Byte(2);
            writer.End(esds);
            writer.End(sample);
        }

        private static int DescriptorBytes(int size)
        {
            int bytes = 2;
            for (int remaining = size >> 7; remaining > 0; remaining >>= 7) { bytes++; }
            return checked(bytes + size);
        }

        private static void Descriptor(Mp4BoxWriter writer, byte tag, int size)
        {
            writer.Byte(tag);
            int shift = 0;
            for (int remaining = size >> 7; remaining > 0; remaining >>= 7) { shift += 7; }
            for (; shift >= 0; shift -= 7)
            { writer.Byte((byte)(((size >> shift) & 127) | (shift > 0 ? 128 : 0))); }
        }
    }
}
