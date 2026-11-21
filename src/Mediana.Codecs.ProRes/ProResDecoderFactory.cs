// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Video;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.ProRes
{
    /// <summary>Decodes ProRes frames through the common video codec and indexed-seek interfaces.</summary>
    public class ProResDecoderFactory(ProResDecoderOptions? options = null, Action<string>? warningHandler = null) : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.prores";
        public string CodecId => ProResFrameHeader.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                return "The stream is not ProRes video.";
            }
            string? reason = StreamFormats.ProRes.GetUnsupportedReason(stream.Format);
            if (reason is not null)
            {
                return reason;
            }

            return stream.Configuration.IsEmpty ? null : "ProRes frame headers require an empty external configuration.";
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new ProResDecoder(stream, allocator, options ?? new(), warningHandler);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new ProResRandomAccessInspector(stream.StreamIndex);
        }
    }
}
