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
            using IWebpImagePayload image = CreatePayload(input, allocator, options, cancellationToken);
            bool separateAlpha = options.Compression == WebpCompression.Lossy && image.HasAlpha;
            int alphaLength = separateAlpha ? checked(1 + input.Format.Width * input.Format.Height) : 0;
            int length = checked(WebpRiff.HeaderBytes + WebpRiff.ChunkLength(image.PayloadLength));
            if (separateAlpha) { length = checked(length + WebpRiff.CanvasChunkBytes + WebpRiff.ChunkLength(alphaLength)); }
            using WritableMediaBuffer output = allocator.Allocate(length);
            WebpRiff.WriteHeader(output.Memory.Span, length);
            int position = WebpRiff.HeaderBytes;
            if (separateAlpha)
            {
                WebpRiff.WriteCanvas(output.Memory.Span[position..], input.Format.Width, input.Format.Height, 16);
                position += WebpRiff.CanvasChunkBytes;
                WebpRiff.WriteChunk(output.Memory.Span[position..], "ALPH"u8, alphaLength);
                WriteAlpha(input, output.Memory.Span.Slice(position + WebpRiff.ChunkHeaderBytes, alphaLength), cancellationToken);
                position += WebpRiff.ChunkLength(alphaLength);
            }
            WebpRiff.WriteChunk(output.Memory.Span[position..], image.FourCc, image.PayloadLength);
            image.Write(output.Memory.Slice(position + WebpRiff.ChunkHeaderBytes, image.PayloadLength), cancellationToken);
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

        private static void WriteAlpha(VideoFrame input, Span<byte> output, CancellationToken token)
        {
            output[0] = 0;
            for (int row = 0; row < input.Format.Height; row++)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> source = input.GetRow(0, row);
                for (int x = 0; x < input.Format.Width; x++) { output[1 + row * input.Format.Width + x] = source[x * 4 + 3]; }
            }
        }

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
