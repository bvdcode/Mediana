// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.H264
{
    /// <summary>Decodes H.264 Annex B access units or byte ranges. Complete access units support indexed
    /// restart inspection. In-band parameter sets and picture ordering follow the AVC decoder's supported syntax.</summary>
    public class H264AnnexBDecoderFactory(H264DecoderOptions? options = null, Action<string>? warningHandler = null)
        : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        private readonly H264DecoderOptions decoderOptions = options ?? new();

        public string ImplementationId => "mediana.h264.annexb";
        public string CodecId => "h264";

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            string? reason = GetUnsupportedFormat(stream);
            return reason is null && stream.Format == StreamFormats.AvcAnnexBAccessUnit
                ? H264Support.GetUnsupportedReason(ReadConfiguration(stream)) : reason;
        }

        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            H264Configuration configuration = ReadSupportedConfiguration(stream);
            H264Decoder decoder = new(stream.StreamIndex, configuration, allocator, decoderOptions,
                stream.ColorDescription, stream.AspectRatioDescription, warningHandler);
            return stream.Format.Boundary == EncodedPacketBoundary.AccessUnit
                ? decoder : new StartCodeVideoDecoder(new(stream.StreamIndex, allocator, Classify), decoder);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream) =>
            new H264RandomAccessInspector(stream, ReadSupportedConfiguration(stream), decoderOptions);

        private H264Configuration ReadSupportedConfiguration(EncodedStreamInfo stream)
        {
            if (GetUnsupportedFormat(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            H264Configuration configuration = ReadConfiguration(stream);
            if (stream.Format == StreamFormats.AvcAnnexBAccessUnit &&
                H264Support.GetUnsupportedReason(configuration) is string unsupported)
            {
                throw new NotSupportedException(unsupported);
            }
            return configuration;
        }

        private string? GetUnsupportedFormat(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                return "The stream is not H.264 video.";
            }
            return stream.Format == StreamFormats.AvcAnnexBAccessUnit
                ? null : StreamFormats.AvcAnnexB.GetUnsupportedReason(stream.Format);
        }

        private H264Configuration ReadConfiguration(EncodedStreamInfo stream)
        {
            decoderOptions.Validate();
            if (stream.Format == StreamFormats.AvcAnnexBAccessUnit)
            {
                return H264Configuration.Parse(stream.Configuration.Span, decoderOptions.HeaderLimits).WithLengthSize(0);
            }
            H264Configuration configuration = H264Configuration.EmptyAnnexB;
            return stream.Configuration.IsEmpty ? configuration
                : configuration.WithParameterSet(stream.Configuration.Span, decoderOptions.HeaderLimits);
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
