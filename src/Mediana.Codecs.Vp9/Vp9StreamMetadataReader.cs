// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Vp9
{
    /// <summary>Reads sample geometry and interpretation from the opening VP9 keyframe.</summary>
    public class Vp9StreamMetadataReader : IPacketPrefixMetadataReader
    {
        public string ImplementationId => "mediana.vp9.metadata";
        public string CodecId => "vp9";
        public int PacketProbeLength => 32;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return null;
        }

        public StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.Vp9 || Vp9FrameHeader.ReadOpeningGeometry(prefix) is not Vp9PictureGeometry geometry)
            {
                return null;
            }
            VideoFormat format = Vp9VideoFormat.Create(geometry, stream);
            VideoChromaSampling sampling = (geometry.SubsamplingX, geometry.SubsamplingY) switch
            {
                (true, true) => VideoChromaSampling.Sampling420,
                (true, false) => VideoChromaSampling.Sampling422,
                (false, true) => VideoChromaSampling.Sampling440,
                (false, false) => VideoChromaSampling.Sampling444
            };
            VideoStreamConfiguration description = new(format.Width, format.Height, sampling, geometry.BitDepth,
                geometry.BitDepth, true, format.SampleAspectRatio, format.GetColorInterpretation())
            {
                CodecProfile = new(FormattableString.Invariant($"Profile {geometry.Profile}")),
                PixelFormat = format.PixelFormat
            };
            return new VideoStreamMetadata([description]);
        }
    }
}
