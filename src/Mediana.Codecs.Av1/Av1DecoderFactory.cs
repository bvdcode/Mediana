// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1
{
    /// <summary>Decodes single-tile Main-profile eight-bit key and single-reference inter pictures with lossless or quantized transforms from low-overhead frame OBUs.
    /// An access unit may contain hidden key pictures followed by one visible picture or reference display.
    /// Monochrome and 4:2:0 output preserve native samples, color labels, render aspect ratio and packet timing.
    /// Variable-size quantized inter transforms and optional reconstruction tools outside this coverage fail explicitly.
    /// Input packets are borrowed; output frames own independent buffer leases. Calls must not overlap.</summary>
    public class Av1DecoderFactory : IVideoDecoderFactory, IRandomAccessDecoderFactory
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

        /// <summary>Recognizes complete reference refreshes at the first coded picture of an access unit.
        /// Hidden reference pictures remain part of the access unit and are decoded before its visible picture.</summary>
        public IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream)
        {
            if (GetUnsupportedReason(stream) is string reason)
            {
                throw new NotSupportedException(reason);
            }
            return new Av1RandomAccessInspector(stream.StreamIndex, Av1Configuration.Read(stream.Configuration.Span));
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
