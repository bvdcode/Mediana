// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Dv
{
    /// <summary>Reads DV25 geometry and scan information from the opening DIF sequence.</summary>
    public class DvStreamMetadataReader : IPacketPrefixMetadataReader
    {
        public string ImplementationId => "mediana.dv.metadata";
        public string CodecId => DvFrameHeader.CodecId;
        public int PacketProbeLength => DvFrameHeader.PrefixBytes;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return null;
        }

        public StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.Dv) { return null; }
            DvFrameHeader header = DvFrameHeader.ReadPrefix(prefix);
            VideoStreamConfiguration metadata = new(DvFrameHeader.Width, header.CodedHeight,
                header.IsNtsc ? VideoChromaSampling.Sampling411 : VideoChromaSampling.Sampling420, 8, 8,
                header.ScanMode == VideoScanMode.Progressive, header.SampleAspectRatio, DvPictureDecoder.GetColor(header))
            {
                PixelFormat = header.IsNtsc ? VideoPixelFormat.Yuv411P8 : VideoPixelFormat.Yuv420P8,
                CodecProfile = new("DV25")
            };
            return new VideoStreamMetadata([metadata]);
        }
    }
}
