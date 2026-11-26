// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Mpeg2
{
    /// <summary>Decodes MPEG-2 Simple and Main profile frame pictures with 8-bit 4:2:0 samples.</summary>
    public class Mpeg2DecoderFactory : IVideoDecoderFactory
    {
        public string ImplementationId => "mediana.mpeg2video";
        public string CodecId => "mpeg2video";

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                return "The stream is not MPEG-2 video.";
            }
            return StreamFormats.Mpeg2Video.GetUnsupportedReason(stream.Format);
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new StartCodeVideoDecoder(new(stream.StreamIndex, allocator, Classify), new Mpeg2VideoDecoder(stream, allocator));
        }

        private static StartCodeUnitKind Classify(ReadOnlySpan<byte> unit) => unit[0] switch
        {
            0x00 => StartCodeUnitKind.Picture,
            0xb3 or 0xb8 => StartCodeUnitKind.NextPicturePrefix,
            _ => StartCodeUnitKind.Other
        };
    }
}
