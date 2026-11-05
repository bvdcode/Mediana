// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Decodes one registered image, document page or animation frame.
        /// Borrows the source; random access preserves its sequential position. Sequential input is consumed on failure.
        /// Encoded input, probe, growth buffers and output frame share the allocator; codec workspace has separate module limits.</summary>
        public async ValueTask<VideoFrame> DecodeImageAsync(IByteSource source, MediaBufferAllocator allocator,
            ImageDecodeRequest? request = null, ImageOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new ImageOpenOptions();
            request ??= new ImageDecodeRequest();
            int probeLength = GetImageProbeLength(options, cancellationToken);
            using WritableMediaBuffer writable = allocator.Allocate(Math.Min(probeLength, options.MaxInputBytes));
            int read = await MediaInputProbe.ReadAsync(source, writable.Memory, cancellationToken).ConfigureAwait(false);
            IImageDecoder? opening = FindImageDecoder(writable.Memory.Span[..read], source.Length ?? (read < writable.Memory.Length ? read : null));
            if (opening is not null) { request.Validate(opening.Features); }
            using MediaBuffer prefix = writable.Publish();
            await using IByteSource replay = MediaInputProbe.Replay(source, prefix, read);
            using MediaBuffer input = await EncodedImageInput.ReadAsync(replay, options.MaxInputBytes, allocator, cancellationToken).ConfigureAwait(false);
            IImageDecoder decoder = FindImageDecoder(input.Memory.Span[..Math.Min(probeLength, input.Memory.Length)], input.Memory.Length)
                ?? throw new NotSupportedException("No registered image decoder recognizes the input.");
            request.Validate(decoder.Features);
            return await imageContext.DecodeAsync(input.Memory, request, allocator, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Decodes complete encoded bytes without copying them into another input/probe buffer.
        /// Borrows the exact supplied memory until completion; the caller keeps it alive and unmodified.
        /// The returned frame owns independent storage. Existing encoded storage belongs to its original allocation budget.
        /// Recognition, feature selection and byte limits are the same as for byte-source input.</summary>
        public ValueTask<VideoFrame> DecodeImageAsync(ReadOnlyMemory<byte> encoded, MediaBufferAllocator allocator,
            ImageDecodeRequest? request = null, ImageOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new ImageOpenOptions();
            request ??= new ImageDecodeRequest();
            int probeLength = GetImageProbeLength(options, cancellationToken);
            if (encoded.Length > options.MaxInputBytes)
            {
                throw new NotSupportedException("The encoded image exceeds the configured input byte limit.");
            }
            IImageDecoder decoder = FindImageDecoder(encoded.Span[..Math.Min(probeLength, encoded.Length)], encoded.Length)
                ?? throw new NotSupportedException("No registered image decoder recognizes the input.");
            request.Validate(decoder.Features);
            return imageContext.DecodeAsync(encoded, request, allocator, cancellationToken);
        }

        private int GetImageProbeLength(ImageOpenOptions options, CancellationToken token)
        {
            options.Validate();
            token.ThrowIfCancellationRequested();
            if (imageDecoders.Length == 0)
            {
                throw new NotSupportedException("No image decoders are registered.");
            }
            if (ImageProbeLength > options.MaxProbeBytes)
            {
                throw new NotSupportedException("Registered image recognition exceeds the configured probe byte limit.");
            }
            return ImageProbeLength;
        }

        /// <summary>Returns the uniquely recognized image implementation, or null. The prefix may be shorter than
        /// ImageProbeLength at EOF. Recognition does not validate the complete file; ambiguity throws.</summary>
        public string? RecognizeImage(ReadOnlySpan<byte> prefix, long? encodedLength = null)
        {
            if (encodedLength < prefix.Length) { throw new ArgumentOutOfRangeException(nameof(encodedLength)); }
            return FindImageDecoder(prefix, encodedLength)?.ImplementationId;
        }

        private IImageDecoder? FindImageDecoder(ReadOnlySpan<byte> prefix, long? encodedLength = null)
            => imageContext.FindDecoder(prefix, encodedLength);
    }
}
