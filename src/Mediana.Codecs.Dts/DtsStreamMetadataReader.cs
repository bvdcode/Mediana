// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    /// <summary>Inspects the opening DTS Core header without reconstructing PCM.</summary>
    public class DtsStreamMetadataReader : IPacketPrefixMetadataReader
    {
        public string ImplementationId => "mediana.dts.metadata";
        public string CodecId => DtsFrameHeader.CodecId;
        public int PacketProbeLength => DtsFrameHeader.MaximumHeaderBytes;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.DtsByteStream) { RequireStream(stream); }
            return null;
        }

        public StreamMetadata ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            RequireStream(stream);
            DtsFrameHeader header = DtsFrameHeader.Read(prefix);
            string[] channels = header.GetChannelOrder();
            return new AudioStreamMetadata(header.SampleRate, channels.Length, channelOrder: channels)
            { CodecProfile = new("DTS Core") };
        }

        private static void RequireStream(EncodedStreamInfo stream)
        {
            if (stream.Kind != MediaKind.Audio || stream.CodecId != DtsFrameHeader.CodecId)
            { throw new ArgumentException("The stream is not DTS audio.", nameof(stream)); }
            StreamFormats.Dts.Require(stream.Format);
            if (!stream.Configuration.IsEmpty) { throw new InvalidDataException("DTS Core frames require an empty external configuration."); }
        }
    }
}
