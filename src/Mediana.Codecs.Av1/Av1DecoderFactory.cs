// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1
{
    /// <summary>Decodes single-tile Main-profile eight-bit key pictures with lossless or four-point quantized transforms from low-overhead frame OBUs.
    /// Monochrome and 4:2:0 output preserve native samples, color labels, render aspect ratio and packet timing.
    /// Inter pictures, larger quantized transforms and optional reconstruction tools outside this coverage fail explicitly.
    /// Input packets are borrowed; output frames own independent buffer leases. Calls must not overlap.</summary>
    public class Av1DecoderFactory : IVideoDecoderFactory
    {
        public string ImplementationId => "mediana.av1";
        public string CodecId => "av1";

        public string? GetUnsupportedReason(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Video || stream.CodecId != CodecId)
            {
                return "The stream is not AV1 video.";
            }
            return StreamFormats.Av1.GetUnsupportedReason(stream.Format);
        }

        /// <summary>Creates a decoder from a version-one av1C configuration record.
        /// Reset restores the original configuration; failure or input cancellation requires reset or disposal.</summary>
        public IMediaCodec<MediaPacket, VideoFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new Av1Decoder(stream, allocator, Av1Configuration.Read(stream.Configuration.Span));
        }
    }
}
