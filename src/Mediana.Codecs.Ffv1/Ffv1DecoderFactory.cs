// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Ffv1
{
    /// <summary>Managed decoder for 8-bit planar FFV1 versions 0, 1 and 3.</summary>
    public class Ffv1DecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.ffv1";
        public string CodecId => Ffv1StreamConfiguration.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                return "The stream does not contain FFV1 video.";
            }
            return StreamFormats.Ffv1.GetUnsupportedReason(stream.Format);
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            Validate(stream);
            return new Ffv1Decoder(stream, Ffv1StreamConfiguration.Read(stream.Configuration), allocator);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            Validate(stream);
            return new Ffv1RandomAccessInspector(stream.StreamIndex);
        }

        private void Validate(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
        }
    }
}
