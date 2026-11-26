// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Dv
{
    /// <summary>Decodes independently accessible PAL and NTSC DV25 video frames to native 8-bit planar YUV.</summary>
    public class DvDecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        public string ImplementationId => "mediana.dv";
        public string CodecId => DvFrameHeader.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return stream.Kind == MediaKind.Video && stream.CodecId == CodecId
                ? StreamFormats.Dv.GetUnsupportedReason(stream.Format) : "The stream does not contain DV video.";
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            Validate(stream);
            return new DvDecoder(stream, allocator);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            Validate(stream);
            return new DvRandomAccessInspector(stream.StreamIndex);
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
