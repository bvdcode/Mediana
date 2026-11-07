// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Hevc
{
    /// <summary>Decodes progressive HEVC Main/Main10 I/P/B pictures from length-prefixed complete access units.
    /// Output preserves native eight/ten-bit YUV 4:2:0 samples, conformance cropping, color labels, SAR and packet timing.</summary>
    public class HevcDecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
    {
        private readonly HevcDecoderOptions options;

        public HevcDecoderFactory(HevcDecoderOptions? options = null)
        {
            this.options = options ?? new(); this.options.Validate();
        }

        public string ImplementationId => "mediana.hevc";
        public string CodecId => "hevc";

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId) { return "The stream is not HEVC video."; }
            string? reason = StreamFormats.Hevc.GetUnsupportedReason(stream.Format);
            return reason ?? HevcConfiguration.Read(stream.Configuration, options).GetUnsupportedReason();
        }

        /// <summary>Returns independently owned frames in display order. Calls must not overlap.
        /// Reset restores the original parameter sets and requires a new IRAP picture; failure or cancellation requires Reset or disposal.</summary>
        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(stream); ArgumentNullException.ThrowIfNull(allocator);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId) { throw new NotSupportedException("The stream is not HEVC video."); }
            StreamFormats.Hevc.Require(stream.Format);
            HevcConfiguration configuration = HevcConfiguration.Read(stream.Configuration, options);
            string? reason = configuration.GetUnsupportedReason();
            if (reason is not null) { throw new NotSupportedException(reason); }
            return new HevcDecoder(stream.StreamIndex, configuration, options, allocator, stream.ColorDescription, stream.AspectRatioDescription);
        }

        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream) => new HevcRandomAccessInspector(stream, options);
    }
}
