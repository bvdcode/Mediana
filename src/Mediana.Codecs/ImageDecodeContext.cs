// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using System.Runtime.CompilerServices;
using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>Immutable decoder registration shared by top-level and embedded images.
    /// Nested calls retain the registry and consume the same bounded nesting allowance.</summary>
    public class ImageDecodeContext
    {
        private readonly IImageDecoder[] decoders;
        private readonly int remainingDepth;
        private readonly MediaResourceSet resources;

        public ImageDecodeContext(IEnumerable<IImageDecoder> decoders, int maximumDepth = 16, MediaResourceSet? resources = null)
        {
            ArgumentNullException.ThrowIfNull(decoders);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
            this.decoders = decoders.ToArray();
            if (this.decoders.Any(item => item is null || string.IsNullOrWhiteSpace(item.ImplementationId) || item.ProbeLength <= 0)
                || this.decoders.Select(item => item.ImplementationId).Distinct(StringComparer.Ordinal).Count() != this.decoders.Length)
            {
                throw new ArgumentException("Supply distinct image implementations with positive probe lengths.", nameof(decoders));
            }
            remainingDepth = maximumDepth;
            this.resources = resources ?? MediaResourceSet.Empty;
        }

        private ImageDecodeContext(ImageDecodeContext parent)
        {
            decoders = parent.decoders;
            remainingDepth = parent.remainingDepth - 1;
            resources = parent.resources;
        }

        public IImageDecoder? FindDecoder(ReadOnlySpan<byte> prefix, long? encodedLength = null)
        {
            IImageDecoder? selected = null;
            foreach (IImageDecoder decoder in decoders)
            {
                if (!decoder.CanDecode(prefix, encodedLength)) { continue; }
                if (selected is not null) { throw new NotSupportedException("Multiple image decoders recognize the input; register one implementation for this format."); }
                selected = decoder;
            }
            return selected;
        }

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) =>
            DecodeCoreAsync(encoded, request, allocator, null, cancellationToken);

        /// <summary>Uses an explicitly registered decoder without content-based substitution; nested images retain the shared registry.</summary>
        public ValueTask<VideoFrame> DecodeUsingAsync(string implementationId, ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
            return DecodeCoreAsync(encoded, request, allocator, implementationId, cancellationToken);
        }

        private async ValueTask<VideoFrame> DecodeCoreAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, string? implementationId, CancellationToken cancellationToken)
        {
            IImageDecoder decoder = Select(encoded, request, allocator, cancellationToken, implementationId);
            VideoFrame frame = await decoder.DecodeAsync(encoded, Descend(request), allocator, cancellationToken).ConfigureAwait(false);
            return ValidateFrame(frame, request, cancellationToken);
        }

        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            IImageDecoder decoder = Select(encoded, request, allocator, cancellationToken);
            if (decoder is not ISynchronousImageDecoder synchronous)
            {
                throw new NotSupportedException("This image implementation requires asynchronous decoding. Use the asynchronous document reader.");
            }
            return ValidateFrame(synchronous.Decode(encoded, Descend(request), allocator, cancellationToken), request, cancellationToken);
        }

        /// <summary>Decodes a sequence in the same nested-image scope as still decoding. Each frame transfers ownership to the caller.</summary>
        public IAsyncEnumerable<VideoFrame> DecodeFramesAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) =>
            DecodeFramesCoreAsync(encoded, request, allocator, null, cancellationToken);

        public IAsyncEnumerable<VideoFrame> DecodeFramesUsingAsync(string implementationId, ReadOnlyMemory<byte> encoded,
            ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
            return DecodeFramesCoreAsync(encoded, request, allocator, implementationId, cancellationToken);
        }

        private async IAsyncEnumerable<VideoFrame> DecodeFramesCoreAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, string? implementationId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            IImageDecoder decoder = Select(encoded, request, allocator, cancellationToken, implementationId);
            if (decoder is not IImageSequenceDecoder sequence)
            {
                throw new NotSupportedException($"Image decoder '{decoder.ImplementationId}' does not expose an animation sequence.");
            }
            ImageDecodeRequest nested = Descend(request);
            await foreach (VideoFrame frame in sequence.DecodeFramesAsync(encoded, nested, allocator, cancellationToken).ConfigureAwait(false))
            {
                yield return ValidateFrame(frame, request, cancellationToken);
            }
        }

        private IImageDecoder Select(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken token, string? implementationId = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            token.ThrowIfCancellationRequested();
            if (remainingDepth == 0) { throw new InvalidDataException("Embedded images exceed the configured decoding depth."); }
            IImageDecoder decoder = implementationId is not null ? GetDecoder(implementationId) :
                FindDecoder(encoded.Span, encoded.Length) ?? throw new NotSupportedException("No registered image decoder recognizes the input.");
            request.Validate(decoder.Features);
            return decoder;
        }

        public IImageDecoder GetDecoder(string implementationId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
            return decoders.SingleOrDefault(decoder => decoder.ImplementationId == implementationId)
                ?? throw new NotSupportedException($"No image decoder is registered as '{implementationId}'.");
        }

        private ImageDecodeRequest Descend(ImageDecodeRequest request) => request with
        {
            Images = new ImageDecodeContext(this),
            Resources = request.Resources ?? resources
        };

        private static VideoFrame ValidateFrame(VideoFrame frame, ImageDecodeRequest request, CancellationToken token)
        {
            bool retained = false;
            try
            {
                token.ThrowIfCancellationRequested();
                if (request.MaximumPixels is int limit && (long)frame.Format.Width * frame.Format.Height > limit)
                {
                    throw new InvalidDataException("Decoded image exceeds MaximumPixels.");
                }
                retained = true;
                return frame;
            }
            finally { if (!retained) { frame.Dispose(); } }
        }
    }
}
