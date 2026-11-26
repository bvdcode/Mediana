// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Ffv1
{
    /// <summary>Reads opening FFV1 stream parameters without reconstructing pixels.</summary>
    public class Ffv1StreamMetadataReader : IPacketPrefixMetadataReader
    {
        public string ImplementationId => "mediana.ffv1.metadata";
        public string CodecId => Ffv1StreamConfiguration.CodecId;
        public int PacketProbeLength => 8192;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.Ffv1) { return null; }
            Ffv1StreamConfiguration config = Ffv1StreamConfiguration.Read(stream.Configuration);
            if (config.ExtraData.IsEmpty) { return null; }
            Ffv1SliceConfiguration external = Ffv1SliceConfiguration.Read(config);
            return Describe(stream, config, external.Format, 3);
        }

        public StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.Ffv1) { return null; }
            Ffv1StreamConfiguration configuration = Ffv1StreamConfiguration.Read(stream.Configuration);
            if (!configuration.ExtraData.IsEmpty) { return null; }
            Ffv1RangeReader reader = new(prefix);
            if (!Ffv1FrameModel.ReadKeyFrame(ref reader)) { return null; }
            Ffv1FrameModel model = Ffv1FrameModel.Read(ref reader);
            return Describe(stream, configuration, model.PixelFormat, model.Version);
        }

        private static StreamMetadata? Describe(EncodedStreamInfo stream, Ffv1StreamConfiguration configuration,
            VideoPixelFormat pixelFormat, int version)
        {
            VideoFormat format = Ffv1VideoFormat.Create(stream, configuration, pixelFormat);
            if (format.PixelFormat is not PlanarVideoPixelFormat planar) { return null; }
            VideoChromaSampling sampling = (planar.HorizontalSubsampling, planar.VerticalSubsampling) switch
            {
                (1, 1) => VideoChromaSampling.Sampling444,
                (2, 1) => VideoChromaSampling.Sampling422,
                (2, 2) => VideoChromaSampling.Sampling420,
                (4, 1) => VideoChromaSampling.Sampling411,
                _ => throw new InvalidOperationException("Unexpected FFV1 chroma subsampling.")
            };
            VideoStreamConfiguration metadata = new(format.Width, format.Height, sampling, 8, 8,
                sampleAspectRatio: stream.AspectRatioDescription?.Resolve(null), color: format.GetColorInterpretation())
            {
                PixelFormat = format.PixelFormat,
                CodecProfile = new("FFV1 version " + version.ToString(CultureInfo.InvariantCulture))
            };
            return new VideoStreamMetadata([metadata]);
        }
    }
}
