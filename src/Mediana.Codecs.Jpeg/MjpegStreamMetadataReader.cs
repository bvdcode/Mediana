// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Reads opening Motion JPEG dimensions, precision and sampling from the JPEG start of frame.</summary>
    public class MjpegStreamMetadataReader : IPacketPrefixMetadataReader
    {
        public string ImplementationId => "mediana.mjpeg.metadata";
        public string CodecId => "mjpeg";
        public int PacketProbeLength => 64 * 1024;

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return null;
        }

        public StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Format != StreamFormats.Mjpeg) { return null; }
            using JpegDocument document = new(prefix.ToArray(), new JpegDecodeOptions(), null, null, CancellationToken.None);
            byte? marker = document.ReadFramePrefix();
            if (marker is null || document.Components.Count is not (1 or 3)) { return null; }
            VideoChromaSampling sampling = VideoChromaSampling.Monochrome;
            VideoPixelFormat? format = null;
            if (document.Components.Count == 3)
            {
                JpegComponent chroma = document.Components[1];
                JpegComponent otherChroma = document.Components[2];
                if (chroma.Horizontal != otherChroma.Horizontal || chroma.Vertical != otherChroma.Vertical ||
                    document.MaximumHorizontal % chroma.Horizontal != 0 || document.MaximumVertical % chroma.Vertical != 0)
                {
                    return null;
                }
                (VideoChromaSampling, VideoPixelFormat?) layout = (document.MaximumHorizontal / chroma.Horizontal,
                    document.MaximumVertical / chroma.Vertical) switch
                {
                    (1, 1) => (VideoChromaSampling.Sampling444, VideoPixelFormat.Yuv444P8),
                    (2, 1) => (VideoChromaSampling.Sampling422, VideoPixelFormat.Yuv422P8),
                    (2, 2) => (VideoChromaSampling.Sampling420, VideoPixelFormat.Yuv420P8),
                    (4, 1) => (VideoChromaSampling.Sampling411, VideoPixelFormat.Yuv411P8),
                    (1, 2) => (VideoChromaSampling.Sampling440, null),
                    _ => (VideoChromaSampling.Monochrome, null)
                };
                if (layout.Item1 == VideoChromaSampling.Monochrome) { return null; }
                (sampling, format) = layout;
            }
            string profile = marker.Value switch
            {
                192 => "Baseline",
                193 => "Extended Sequential",
                194 => "Progressive",
                _ => throw new InvalidOperationException("Unexpected JPEG start of frame.")
            };
            VideoStreamConfiguration description = new(document.Width, document.Height, sampling, 8,
                sampling == VideoChromaSampling.Monochrome ? null : 8,
                sampleAspectRatio: stream.AspectRatioDescription?.Resolve(null),
                color: new(Range: ColorRange.Full))
            {
                CodecProfile = new(profile),
                PixelFormat = format
            };
            return new VideoStreamMetadata([description]);
        }
    }
}
