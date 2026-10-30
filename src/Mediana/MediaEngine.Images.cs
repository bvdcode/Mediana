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
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (imageDecoders.Length == 0)
            {
                throw new NotSupportedException("No image decoders are registered.");
            }
            int probeLength = imageDecoders.Max(item => item.ProbeLength);
            if (probeLength > options.MaxProbeBytes)
            {
                throw new NotSupportedException("Registered image recognition exceeds the configured probe byte limit.");
            }
            using WritableMediaBuffer writable = allocator.Allocate(Math.Min(probeLength, options.MaxInputBytes));
            int read = await MediaInputProbe.ReadAsync(source, writable.Memory, cancellationToken).ConfigureAwait(false);
            IImageDecoder decoder = SelectImageDecoder(writable.Memory.Span[..read]);
            request.Validate(decoder.Features);
            using MediaBuffer prefix = writable.Publish();
            await using IByteSource replay = MediaInputProbe.Replay(source, prefix, read);
            using MediaBuffer input = await EncodedImageInput.ReadAsync(replay, options.MaxInputBytes, allocator, cancellationToken).ConfigureAwait(false);
            VideoFrame frame = await decoder.DecodeAsync(input.Memory, request, allocator, cancellationToken).ConfigureAwait(false);
            bool transferred = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                transferred = true;
                return frame;
            }
            finally
            {
                if (!transferred)
                {
                    frame.Dispose();
                }
            }
        }

        private IImageDecoder SelectImageDecoder(ReadOnlySpan<byte> prefix)
        {
            IImageDecoder? selected = null;
            foreach (IImageDecoder decoder in imageDecoders)
            {
                if (decoder.CanDecode(prefix))
                {
                    if (selected is not null)
                    {
                        throw new NotSupportedException("Multiple image decoders recognize the input; register one implementation for this format.");
                    }
                    selected = decoder;
                }
            }
            return selected ?? throw new NotSupportedException("No registered image decoder recognizes the input.");
        }
    }
}
