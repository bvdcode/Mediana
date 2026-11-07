// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Opus
{
    /// <summary>Managed Opus decoding to 48 kHz interleaved float32 PCM, including header gain,
    /// original-stream pre-skip, packet end trimming and channel mapping families 0, 1 and 255.</summary>
    public class OpusDecoderFactory : IAudioDecoderFactory
    {
        public string ImplementationId => "mediana.opus";
        public string CodecId => OpusConfiguration.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId) { return "The stream is not Opus audio."; }
            string? reason = StreamFormats.Opus.GetUnsupportedReason(stream.Format);
            if (reason is not null) { return reason; }
            OpusConfiguration configuration = OpusConfiguration.Read(stream.Configuration.Span);
            return configuration.MappingFamily switch
            {
                0 or 1 or 255 => null,
                _ => "The Opus channel mapping family is not supported."
            };
        }

        /// <summary>Calls must not overlap. Failure or cancellation requires reset or disposal.
        /// Reset restarts original-stream priming. Indexed seeking uses IPacketPrerollDecoder to distinguish
        /// the original beginning from a midstream restart. Packet Duration may trim the decoded tail.</summary>
        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null) { throw new NotSupportedException(reason); }
            return new OpusDecoder(stream.StreamIndex, OpusConfiguration.Read(stream.Configuration.Span), allocator);
        }
    }
}
