// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Matroska
{
    /// <summary>Codec representation and known decoded frame period, independent of native track metadata.</summary>
    internal record MatroskaCodecConfiguration(EncodedStreamInfo Stream, MediaDuration? FrameDuration,
        Mp3FrameHeader? MpegAudioHeader = null)
    {
        public void ValidatePacket(ReadOnlySpan<byte> data)
        {
            if (MpegAudioHeader is not Mp3FrameHeader original)
            {
                return;
            }
            if (data.Length < Mp3FrameHeader.HeaderBytes)
            {
                throw new InvalidDataException("The Matroska MP3 frame header is truncated.");
            }
            Mp3FrameHeader header = Mp3FrameHeader.Read(data[..Mp3FrameHeader.HeaderBytes]);
            if (header.FrameBytes != data.Length)
            {
                throw new InvalidDataException("A Matroska MP3 frame must contain exactly one complete Layer III frame.");
            }
            if (header.Version != original.Version || header.SampleRate != original.SampleRate ||
                header.ChannelCount != original.ChannelCount)
            {
                throw new NotSupportedException("Changes to the Matroska MP3 sampling format are not supported.");
            }
        }
    }
}
