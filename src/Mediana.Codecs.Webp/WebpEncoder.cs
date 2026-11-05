// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    /// <summary>Managed VP8L encoding with exact RGBA preservation, including RGB under zero alpha.</summary>
    public static class WebpEncoder
    {
        private const int ContainerBytes = 20;

        /// <summary>Encodes straight RGBA32 sRGB with square pixels. Borrows input and returns an owned complete RIFF file.
        /// Working pixel, token, hash and output arrays are charged to the allocator; small coding tables and object overhead are not.
        /// Output capacity equals the encoded length, including RIFF padding.</summary>
        public static MediaBuffer Encode(VideoFrame input, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ValidateInput(input, cancellationToken);
            using WebpLosslessImage image = WebpLosslessImage.Create(input, allocator, cancellationToken);
            int length = checked(ContainerBytes + image.PayloadLength + (image.PayloadLength & 1));
            using WritableMediaBuffer output = allocator.Allocate(length);
            "RIFF"u8.CopyTo(output.Memory.Span);
            BinaryPrimitives.WriteUInt32LittleEndian(output.Memory.Span[4..], (uint)(length - 8));
            "WEBPVP8L"u8.CopyTo(output.Memory.Span[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(output.Memory.Span[16..], (uint)image.PayloadLength);
            image.Write(output.Memory.Slice(ContainerBytes, image.PayloadLength), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return output.Publish();
        }

        /// <summary>Writes a complete lossless WebP to a writable, possibly non-seekable destination.
        /// Retains input until completion, leaves destination open and does not flush it. Failed output must be discarded.</summary>
        public static async Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(input);
            if (!destination.CanWrite) { throw new ArgumentException("The WebP destination must be writable.", nameof(destination)); }
            using VideoFrame source = input.Retain();
            using MediaBuffer encoded = Encode(source, allocator, cancellationToken);
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
