// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    /// <summary>Managed Microsoft MPEG-4 v3 or WMV2 decoding into 8-bit YUV 4:2:0.</summary>
    public class MicrosoftMpeg4DecoderFactory(string codecId) : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        private readonly EncodedStreamFormat format = codecId switch
        {
            MicrosoftMpeg4Configuration.Version3CodecId => StreamFormats.MicrosoftMpeg4V3,
            MicrosoftMpeg4Configuration.Wmv2CodecId => StreamFormats.Wmv2,
            _ => throw new ArgumentOutOfRangeException(nameof(codecId))
        };

        public string ImplementationId => $"mediana.{CodecId}";
        public string CodecId { get; } = codecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                return $"The stream is not {CodecId} video.";
            }
            return format.GetUnsupportedReason(stream.Format);
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            Validate(stream);
            MicrosoftMpeg4Configuration configuration = MicrosoftMpeg4Configuration.Read(stream.Configuration);
            return new MicrosoftMpeg4Decoder(stream, allocator, configuration, CodecId == MicrosoftMpeg4Configuration.Wmv2CodecId);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            Validate(stream);
            return new MicrosoftMpeg4RandomAccessInspector(stream.StreamIndex, CodecId == MicrosoftMpeg4Configuration.Wmv2CodecId);
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
