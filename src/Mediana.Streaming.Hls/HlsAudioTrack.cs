// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;

namespace Mediana.Streaming.Hls
{
    internal class HlsAudioTrack(EncodedStreamInfo stream)
    {
        public EncodedStreamInfo Stream { get; } = stream;
        public string Codecs { get; } = (stream.Kind, stream.CodecId) switch
        {
            (MediaKind.Audio, AacConfiguration.CodecId) => AacCodecs(stream),
            (MediaKind.Audio, Mp3FrameHeader.CodecId) => Mp3Codecs(stream),
            _ => throw new NotSupportedException("AVC HLS audio requires AAC-LC or MPEG-1/2 Layer III.")
        };

        private static string AacCodecs(EncodedStreamInfo stream)
        {
            _ = AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out string? reason)
                ?? throw new NotSupportedException(reason);
            return "mp4a.40.2";
        }

        private static string Mp3Codecs(EncodedStreamInfo stream) => Mp3FrameHeader.Read(stream.Configuration.Span).Version switch
        {
            MpegAudioVersion.Mpeg1 => "mp4a.6B",
            MpegAudioVersion.Mpeg2 => "mp4a.69",
            MpegAudioVersion.Mpeg25 => throw new NotSupportedException("MPEG-2.5 Layer III is not supported in MPEG-TS."),
            _ => throw new InvalidDataException("The MPEG audio version is invalid.")
        };
    }
}
