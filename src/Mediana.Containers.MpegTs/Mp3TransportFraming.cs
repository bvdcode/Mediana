// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Containers.MpegTs
{
    internal class Mp3TransportFraming
    {
        private readonly Mp3FrameHeader configuration;

        public Mp3TransportFraming(ReadOnlySpan<byte> bytes)
        {
            configuration = Mp3FrameHeader.Read(bytes);
            StreamType = configuration.Version switch
            {
                MpegAudioVersion.Mpeg1 => 0x03,
                MpegAudioVersion.Mpeg2 => 0x04,
                MpegAudioVersion.Mpeg25 => throw new NotSupportedException("MPEG-2.5 Layer III is not supported in MPEG-TS."),
                _ => throw new InvalidDataException("The MPEG audio version is invalid.")
            };
        }

        public byte StreamType { get; }

        public MediaBuffer Convert(ReadOnlySpan<byte> data, MediaBufferAllocator allocator)
        {
            if (data.Length < Mp3FrameHeader.HeaderBytes)
            {
                throw new InvalidDataException("The MPEG audio frame header is truncated.");
            }
            Mp3FrameHeader header = Mp3FrameHeader.Read(data[..Mp3FrameHeader.HeaderBytes]);
            if (header.FrameBytes != data.Length)
            {
                throw new InvalidDataException("Each MP3 packet must contain exactly one complete frame.");
            }
            if (header.Version != configuration.Version || header.SampleRate != configuration.SampleRate ||
                header.ChannelCount != configuration.ChannelCount)
            {
                throw new NotSupportedException("MPEG audio format changes are not supported by the TS writer.");
            }
            using WritableMediaBuffer writable = allocator.Allocate(data.Length);
            data.CopyTo(writable.Memory.Span);
            return writable.Publish();
        }
    }
}
