// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Alac
{
    /// <summary>Decodes independent ALAC access units to interleaved, left-aligned signed32 PCM.</summary>
    public class AlacDecoderFactory : IAudioDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.alac";
        public string CodecId => AlacConfiguration.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                return "The stream is not ALAC audio.";
            }
            string? reason = StreamFormats.Alac.GetUnsupportedReason(stream.Format);
            if (reason is not null)
            {
                return reason;
            }
            _ = AlacConfiguration.Read(stream.Configuration.Span);
            return null;
        }

        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new AlacDecoder(stream.StreamIndex, AlacConfiguration.Read(stream.Configuration.Span), allocator);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new AlacRandomAccessInspector(stream.StreamIndex);
        }
    }
}
