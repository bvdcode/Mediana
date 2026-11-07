// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    /// <summary>Managed Vorbis I decoding to native-rate interleaved float32 PCM in Vorbis channel order.</summary>
    public class VorbisDecoderFactory : IAudioDecoderFactory
    {
        public string ImplementationId => "mediana.vorbis";
        public string CodecId => VorbisIdentification.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId) { return "The stream is not Vorbis audio."; }
            string? reason = StreamFormats.Vorbis.GetUnsupportedReason(stream.Format);
            if (reason is not null) { return reason; }
            VorbisConfiguration.Read(stream.Configuration);
            return null;
        }

        /// <summary>Calls must not overlap. Failure or cancellation requires reset or disposal.
        /// The first audio packet primes overlap without publishing PCM. Indexed seeking requires one preceding packet.
        /// Packet Duration may trim the decoded tail to an exact sample count.</summary>
        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null) { throw new NotSupportedException(reason); }
            return new VorbisDecoder(stream.StreamIndex, VorbisConfiguration.Read(stream.Configuration), allocator);
        }
    }
}
