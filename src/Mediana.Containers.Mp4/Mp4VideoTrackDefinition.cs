// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Containers.Mp4
{
    /// <summary>An AVC or HEVC video track for newly encoded ISO BMFF fragments.</summary>
    public class Mp4VideoTrackDefinition
    {
        public Mp4VideoTrackDefinition(EncodedStreamInfo stream, VideoFormat format, uint trackId = 1)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(format);
            ArgumentOutOfRangeException.ThrowIfZero(trackId);
            ArgumentOutOfRangeException.ThrowIfEqual(trackId, uint.MaxValue);
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
            Stream = stream;
            Format = format;
            TrackId = trackId;
            TimeBase = new(1, stream.TimeBase.Denominator);
            SampleAspectRatio = stream.AspectRatioDescription is VideoAspectRatioDescription aspect
                ? aspect.Resolve(format.SampleAspectRatio) : format.SampleAspectRatio;
        }

        public EncodedStreamInfo Stream { get; }
        public VideoFormat Format { get; }
        public uint TrackId { get; }
        public TimeBase TimeBase { get; }
        public SampleAspectRatio? SampleAspectRatio { get; }
        internal string SampleEntry { get; }
        internal string ConfigurationBox { get; }
    }
}
