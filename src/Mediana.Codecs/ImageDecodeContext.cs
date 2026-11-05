// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs
{
    /// <summary>Immutable decoder registration shared by top-level and embedded images.
    /// Nested calls retain the registry and consume the same bounded nesting allowance.</summary>
    public class ImageDecodeContext
    {
        private readonly IImageDecoder[] decoders;
        private readonly int remainingDepth;

        public ImageDecodeContext(IEnumerable<IImageDecoder> decoders, int maximumDepth = 16)
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
        }

        private ImageDecodeContext(ImageDecodeContext parent)
        {
            decoders = parent.decoders;
            remainingDepth = parent.remainingDepth - 1;
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

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            IImageDecoder decoder = Select(encoded, request, allocator, cancellationToken);
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
        public async IAsyncEnumerable<VideoFrame> DecodeFramesAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IImageDecoder decoder = Select(encoded, request, allocator, cancellationToken);
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

        private IImageDecoder Select(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            token.ThrowIfCancellationRequested();
            if (remainingDepth == 0) { throw new InvalidDataException("Embedded images exceed the configured decoding depth."); }
            IImageDecoder decoder = FindDecoder(encoded.Span, encoded.Length)
                ?? throw new NotSupportedException("No registered image decoder recognizes the input.");
            request.Validate(decoder.Features);
            return decoder;
        }

        private ImageDecodeRequest Descend(ImageDecodeRequest request) => request with { Images = new ImageDecodeContext(this) };

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
