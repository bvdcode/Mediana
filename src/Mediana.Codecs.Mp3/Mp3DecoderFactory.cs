// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Mp3
{
    /// <summary>Decodes MPEG-1/2/2.5 Layer III frames to native-endian interleaved float32 PCM.
    /// Configuration contains the first four-byte frame header; each packet contains one complete frame.
    /// Encoder delay and end padding are retained. Free-format streams and de-emphasis are not supported.</summary>
    public class Mp3DecoderFactory : IAudioDecoderFactory
    {
        private readonly int maxFrameBytes;

        public Mp3DecoderFactory(int maxFrameBytes = 4096)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameBytes, Mp3FrameHeader.HeaderBytes);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFrameBytes, int.MaxValue / 8 - 511);
            this.maxFrameBytes = maxFrameBytes;
        }

        public string ImplementationId => "mediana.mp3";
        public string CodecId => Mp3FrameHeader.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                return "The stream is not MPEG Layer III audio.";
            }
            Mp3FrameHeader header = Mp3FrameHeader.Read(stream.Configuration.Span);
            if (header.Emphasis != 0)
            {
                return "MP3 de-emphasis is not supported.";
            }
            return header.FrameBytes > maxFrameBytes ? "The MP3 frame exceeds its configured byte limit." : null;
        }

        /// <summary>Calls must not overlap. Failure or cancellation requires reset or disposal.
        /// Reset clears the bit reservoir and synthesis history; resume from the beginning for continuous output.</summary>
        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            return new Mp3Decoder(stream.StreamIndex, Mp3FrameHeader.Read(stream.Configuration.Span), allocator, maxFrameBytes);
        }
    }
}
