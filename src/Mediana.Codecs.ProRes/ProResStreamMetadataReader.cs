// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.ProRes
{
    /// <summary>Reads ProRes geometry, precision, field order and color from the opening frame header.</summary>
    public class ProResStreamMetadataReader : IPacketPrefixMetadataReader
    {
        public string ImplementationId => "mediana.prores.metadata";
        public string CodecId => ProResFrameHeader.CodecId;
        public int PacketProbeLength => 28 + 2 * 64;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            RequireStream(stream);
            return null;
        }

        public StreamMetadata ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            RequireStream(stream);
            ProResFrameHeader header = ProResFrameHeader.Read(prefix.ToArray());
            VideoChromaSampling sampling = header.ChromaFormat switch
            {
                2 => VideoChromaSampling.Sampling422,
                3 => VideoChromaSampling.Sampling444,
                _ => throw new InvalidDataException("The ProRes chroma format is reserved.")
            };
            return new VideoStreamMetadata([new(header.Width, header.Height, sampling, header.BitDepth,
                header.BitDepth, header.InterlaceMode == 0, header.SampleAspectRatio, header.Color)
            {
                PixelFormat = ProResPictureDecoder.GetPixelFormat(header)
            }]);
        }

        private static void RequireStream(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != ProResFrameHeader.CodecId)
            {
                throw new ArgumentException("The stream is not ProRes video.", nameof(stream));
            }
            StreamFormats.ProRes.Require(stream.Format);
            if (!stream.Configuration.IsEmpty)
            {
                throw new InvalidDataException("ProRes frame headers require an empty external configuration.");
            }
        }
    }
}
