// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    /// <summary>Managed VP8/VP8L image encoding. Lossless is the default and preserves every RGBA byte.</summary>
    public static class WebpEncoder
    {
        private static readonly WebpEncodingOptions DefaultOptions = new();
        /// <summary>Encodes straight RGBA32 sRGB with square pixels. Borrows input and returns an owned complete RIFF file.
        /// Working pixel, token, hash and output arrays are charged to the allocator; small coding tables and object overhead are not.
        /// Output capacity equals the encoded length, including RIFF padding.</summary>
        public static MediaBuffer Encode(VideoFrame input, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
            => Encode(input, allocator, DefaultOptions, cancellationToken);

        /// <summary>Encodes a complete WebP using explicit compression and quality settings.</summary>
        public static MediaBuffer Encode(VideoFrame input, MediaBufferAllocator allocator, WebpEncodingOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            ValidateInput(input, cancellationToken);
            using WebpFramePayload image = WebpFramePayload.Create(input, allocator, options, cancellationToken);
            byte metadataFlags = WebpMetadataWriter.Flags(options.Metadata);
            bool extended = metadataFlags != 0 || (options.Compression == WebpCompression.Lossy && image.HasAlpha);
            int length = checked(WebpRiff.HeaderBytes + image.Length + WebpMetadataWriter.Length(options.Metadata)
                + (extended ? WebpRiff.CanvasChunkBytes : 0));
            using WritableMediaBuffer output = allocator.Allocate(length);
            WebpRiff.WriteHeader(output.Memory.Span, length);
            int position = WebpRiff.HeaderBytes;
            if (extended)
            {
                byte flags = (byte)(metadataFlags | (image.HasAlpha ? 16 : 0));
                WebpRiff.WriteCanvas(output.Memory.Span[position..], input.Format.Width, input.Format.Height, flags);
                position += WebpRiff.CanvasChunkBytes;
            }
            position += WebpMetadataWriter.WriteProfile(output.Memory.Span[position..], options.Metadata);
            image.Write(output.Memory.Slice(position, image.Length), cancellationToken);
            position += image.Length;
            position += WebpMetadataWriter.WriteDescriptions(output.Memory.Span[position..], options.Metadata);
            if (position != length) { throw new InvalidOperationException("WebP chunk lengths do not match the RIFF size."); }
            cancellationToken.ThrowIfCancellationRequested();
            return output.Publish();
        }

        internal static IWebpImagePayload CreatePayload(VideoFrame input, MediaBufferAllocator allocator, WebpEncodingOptions options, CancellationToken token) =>
            options.Compression switch
            {
                WebpCompression.Lossless => WebpLosslessImage.Create(input, allocator, token),
                WebpCompression.Lossy => Vp8LossyImage.Create(input, options.Quality, allocator, token),
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            };

        /// <summary>Writes a complete lossless WebP to a writable, possibly non-seekable destination.
        /// Retains input until completion, leaves destination open and does not flush it. Failed output must be discarded.</summary>
        public static Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
            => EncodeAsync(input, destination, allocator, DefaultOptions, cancellationToken);

        /// <summary>Writes a complete WebP using explicit options, retaining input until the asynchronous write completes.</summary>
        public static async Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            WebpEncodingOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(input);
            if (!destination.CanWrite) { throw new ArgumentException("The WebP destination must be writable.", nameof(destination)); }
            using VideoFrame source = input.Retain();
            using MediaBuffer encoded = Encode(source, allocator, options, cancellationToken);
            await destination.WriteAsync(encoded.Memory, cancellationToken).ConfigureAwait(false);
            return encoded.Memory.Length;
        }

        internal static void ValidateInput(VideoFrame input, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(input);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(input.Format.PixelFormat, VideoPixelFormat.Rgba32) || input.Format.GetColorInterpretation() != VideoColorInfo.Srgb)
            {
                throw new NotSupportedException("WebP encoding requires straight RGBA32 pixels in full-range sRGB.");
            }
            if (input.Format.SampleAspectRatio != SampleAspectRatio.Square)
            {
                throw new NotSupportedException("WebP encoding requires square pixels; resize non-square samples before encoding.");
            }
            if (input.Format.Width > 16384 || input.Format.Height > 16384)
            {
                throw new NotSupportedException("VP8L dimensions cannot exceed 16384 pixels per axis.");
            }
        }
    }
}
