// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Video;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Containers.Mp4
{
    /// <summary>An AVC or HEVC video track for newly encoded ISO BMFF fragments.</summary>
    public class Mp4VideoTrackDefinition : Mp4TrackDefinition
    {
        public Mp4VideoTrackDefinition(EncodedStreamInfo stream, VideoFormat format, uint trackId = 1, long timestampOffset = 0, MediaTimestamp? presentationEnd = null)
            : base(stream, trackId, timestampOffset, presentationEnd)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(format.Width, ushort.MaxValue);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(format.Height, ushort.MaxValue);
            if (stream.Kind != MediaKind.Video || stream.DisplayMatrix is null)
            {
                throw new ArgumentException("A fragmented video track requires video and an explicit display matrix.", nameof(stream));
            }
            stream.DisplayMatrix.Value.Validate();
            switch (stream.CodecId)
            {
                case "h264":
                    StreamFormats.Avc.Require(stream.Format);
                    if (stream.Configuration.Length < 7 || stream.Configuration.Span[0] != 1)
                    {
                        throw new InvalidDataException("The AVC decoder configuration is invalid.");
                    }
                    SampleEntry = "avc3";
                    ConfigurationBox = "avcC";
                    break;
                case HevcDecoderConfiguration.CodecId:
                    StreamFormats.Hevc.Require(stream.Format);
                    HevcDecoderConfiguration.Read(stream.Configuration);
                    SampleEntry = "hev1";
                    ConfigurationBox = "hvcC";
                    break;
                default:
                    throw new NotSupportedException("Fragmented video writing requires AVC or HEVC access units.");
            }
            Format = format;
            SampleAspectRatio = stream.AspectRatioDescription is VideoAspectRatioDescription aspect
                ? aspect.Resolve(format.SampleAspectRatio) : format.SampleAspectRatio;
        }

        public VideoFormat Format { get; }
        public SampleAspectRatio? SampleAspectRatio { get; }
        internal string SampleEntry { get; }
        internal string ConfigurationBox { get; }

        internal override void WritePresentationSize(Mp4BoxWriter writer)
        {
            double width = Format.Width;
            if (SampleAspectRatio is SampleAspectRatio aspect) { width = width * aspect.Numerator / aspect.Denominator; }
            writer.UInt32(checked((uint)Math.Round(width * 65536, MidpointRounding.ToEven)));
            writer.UInt32(checked((uint)Format.Height << 16));
        }

        internal override void WriteMediaHeader(Mp4BoxWriter writer)
        {
            int vmhd = writer.Full("vmhd", 1);
            writer.Zeros(8);
            writer.End(vmhd);
        }

        internal override void WriteSampleEntry(Mp4BoxWriter writer)
        {
            int sample = writer.Begin(SampleEntry);
            writer.Zeros(6);
            writer.UInt16(1);
            writer.Zeros(16);
            writer.UInt16((ushort)Format.Width);
            writer.UInt16((ushort)Format.Height);
            writer.UInt32(72 << 16);
            writer.UInt32(72 << 16);
            writer.UInt32(0);
            writer.UInt16(1);
            writer.Zeros(32);
            writer.UInt16(24);
            writer.UInt16(ushort.MaxValue);
            writer.Box(ConfigurationBox, Stream.Configuration.Span);
            if (SampleAspectRatio is SampleAspectRatio aspect)
            {
                int pasp = writer.Begin("pasp");
                writer.UInt32((uint)aspect.Numerator);
                writer.UInt32((uint)aspect.Denominator);
                writer.End(pasp);
            }
            writer.End(sample);
        }
    }
}
