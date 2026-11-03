// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Core.Audio;

using Mediana.Formats.Audio;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal class HlsAudioTrack
    {
        public HlsAudioTrack(EncodedStreamInfo stream)
        {
            Stream = stream;
            (Codecs, CodedDuration, Channels) = (stream.Kind, stream.CodecId) switch
            {
                (MediaKind.Audio, AacConfiguration.CodecId) => AacInfo(stream),
                (MediaKind.Audio, Mp3FrameHeader.CodecId) => Mp3Info(stream),
                _ => throw new NotSupportedException("HLS audio requires AAC-LC or MPEG-1/2 Layer III.")
            };
        }

        public EncodedStreamInfo Stream { get; }
        public string Codecs { get; }
        public MediaDuration CodedDuration { get; }
        public int Channels { get; }

        private static (string Codecs, MediaDuration Duration, int Channels) AacInfo(EncodedStreamInfo stream)
        {
            StreamFormats.Aac.Require(stream.Format);
            AacConfiguration configuration = AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out string? reason)
                ?? throw new NotSupportedException(reason);
            return ("mp4a.40.2", new(configuration.SamplesPerFrame, new(1, configuration.SampleRate)), configuration.ChannelCount);
        }

        private static (string Codecs, MediaDuration Duration, int Channels) Mp3Info(EncodedStreamInfo stream)
        {
            StreamFormats.Mp3.Require(stream.Format);
            Mp3FrameHeader header = Mp3FrameHeader.Read(stream.Configuration.Span);
            string codecs = header.Version switch
            {
                MpegAudioVersion.Mpeg1 => "mp4a.6B",
                MpegAudioVersion.Mpeg2 => "mp4a.69",
                MpegAudioVersion.Mpeg25 => throw new NotSupportedException("MPEG-2.5 Layer III is not supported in MPEG-TS."),
                _ => throw new InvalidDataException("The MPEG audio version is invalid.")
            };
            return (codecs, new(header.SamplesPerFrame, new(1, header.SampleRate)), header.ChannelCount);
        }
    }
}
