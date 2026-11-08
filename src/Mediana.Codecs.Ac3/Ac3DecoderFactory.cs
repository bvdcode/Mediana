// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    /// <summary>Managed AC-3 decoder accepting one complete, CRC-protected syncframe per packet.
    /// Output preserves coded channel order in native-endian interleaved float32 PCM.</summary>
    public class Ac3DecoderFactory(bool applyDynamicRangeCompression = true) : IAudioDecoderFactory
    {
        public virtual string CodecId => Ac3FrameHeader.CodecId;
        public virtual string ImplementationId => "mediana.ac3";
        protected virtual bool Enhanced => false;
        protected virtual EncodedStreamFormat ExpectedFormat => StreamFormats.Ac3;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId) { return "The stream is not the expected AC-3 audio codec."; }
            string? reason = ExpectedFormat.GetUnsupportedReason(stream.Format);
            if (reason is not null) { return reason; }
            return stream.Configuration.IsEmpty ? null : "AC-3 syncframes are self-describing and require an empty external configuration.";
        }

        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            string? reason = GetUnsupportedReason(stream);
            if (reason is not null) { throw new NotSupportedException(reason); }
            return new Ac3Decoder(stream.StreamIndex, Enhanced, allocator, applyDynamicRangeCompression);
        }
    }
}
