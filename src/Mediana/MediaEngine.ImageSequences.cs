// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using System.Runtime.CompilerServices;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Retains complete encoded input once during enumeration; source and allocator remain borrowed.
        /// Each yielded frame transfers independent ownership to the caller.</summary>
        public async IAsyncEnumerable<VideoFrame> DecodeImageFramesAsync(IByteSource source, MediaBufferAllocator allocator,
            ImageDecodeRequest? request = null, ImageOpenOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            using MediaBuffer encoded = await EncodedImageInput.ReadAsync(source, options.MaxInputBytes, allocator, cancellationToken).ConfigureAwait(false);
            await foreach (VideoFrame frame in DecodeImageFramesAsync(encoded.Memory, allocator, request, options, cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
        }

        /// <summary>Decodes an animation using a registered sequence decoder. Borrows encoded bytes throughout enumeration;
        /// each yielded frame transfers ownership to the caller. Input is parsed once by the sequence decoder.</summary>
        public IAsyncEnumerable<VideoFrame> DecodeImageFramesAsync(ReadOnlyMemory<byte> encoded, MediaBufferAllocator allocator,
            ImageDecodeRequest? request = null, ImageOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            request ??= new();
            int probeLength = GetImageProbeLength(options, cancellationToken);
            if (encoded.Length > options.MaxInputBytes) { throw new NotSupportedException("The encoded image exceeds the configured input byte limit."); }
            IImageDecoder decoder = FindImageDecoder(encoded.Span[..Math.Min(probeLength, encoded.Length)], encoded.Length)
                ?? throw new NotSupportedException("No registered image decoder recognizes the input.");
            request.Validate(decoder.Features);
            if (decoder is not IImageSequenceDecoder sequence)
            {
                throw new NotSupportedException($"Image decoder '{decoder.ImplementationId}' does not expose an animation sequence.");
            }
            return sequence.DecodeFramesAsync(encoded, request, allocator, cancellationToken);
        }
    }
}
