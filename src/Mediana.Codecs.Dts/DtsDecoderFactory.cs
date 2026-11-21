// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    /// <summary>Decodes DTS Core frames to interleaved float32 PCM, preserving coded channel order.</summary>
    public class DtsDecoderFactory : IAudioDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.dts";
        public string CodecId => DtsFrameHeader.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                return "The stream is not DTS audio.";
            }
            string? reason = StreamFormats.Dts.GetUnsupportedReason(stream.Format);
            if (reason is not null)
            {
                return reason;
            }
            return stream.Configuration.IsEmpty ? null : "DTS Core frames require an empty external configuration.";
        }

        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new DtsDecoder(stream.StreamIndex, allocator);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new DtsRandomAccessInspector(stream.StreamIndex);
        }
    }
}
