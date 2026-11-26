// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.H264
{
    /// <summary>Decodes H.264 Annex B input that may split or combine access units at packet boundaries.
    /// In-band parameter sets and picture ordering follow the AVC decoder's supported syntax.</summary>
    public class H264AnnexBDecoderFactory(H264DecoderOptions? options = null, Action<string>? warningHandler = null)
        : IVideoDecoderFactory
    {
        private readonly H264DecoderOptions decoderOptions = options ?? new();

        public string ImplementationId => "mediana.h264.annexb";
        public string CodecId => "h264";

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                return "The stream is not H.264 video.";
            }
            return StreamFormats.AvcAnnexB.GetUnsupportedReason(stream.Format);
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            decoderOptions.Validate();
            H264Configuration configuration = new(0, new Dictionary<int, H264SequenceParameterSet>(),
                new Dictionary<int, H264PictureParameterSet>());
            if (!stream.Configuration.IsEmpty)
            {
                configuration = configuration.WithParameterSet(stream.Configuration.Span, decoderOptions);
            }
            H264Decoder decoder = new(stream.StreamIndex, configuration, allocator, decoderOptions,
                stream.ColorDescription, stream.AspectRatioDescription, warningHandler);
            return new StartCodeVideoDecoder(new(stream.StreamIndex, allocator, Classify), decoder);
        }

        private static StartCodeUnitKind Classify(ReadOnlySpan<byte> unit)
        {
            int type = H264Nal.Type(unit);
            return type switch
            {
                1 or 5 when unit.Length < 2 => StartCodeUnitKind.Incomplete,
                1 or 5 when (unit[1] & 128) != 0 => StartCodeUnitKind.Picture,
                1 or 5 => StartCodeUnitKind.PictureContinuation,
                6 or 7 or 8 or 9 => StartCodeUnitKind.NextPicturePrefix,
                _ => StartCodeUnitKind.Other
            };
        }
    }
}
