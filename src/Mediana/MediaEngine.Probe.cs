// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.IO;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Inspects recognized media without decoding pictures, audio samples or document pages.
        /// Readers may index the complete input and inspect codec headers or configured payload checksums.
        /// Borrows the source; random access preserves its cursor.
        /// Metadata is independent of source lifetime. Sequential input is consumed on failure.</summary>
        public async ValueTask<MediaInfo> ProbeAsync(IByteSource source, MediaOpenOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new MediaOpenOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (imageDecoders.Length == 0)
            {
                await using ContainerSession session = await OpenContainerAsync(source, options, cancellationToken).ConfigureAwait(false);
                return await ReadContainerInfoAsync(session.Reader, session.Allocator, options.MaxProbeBytes, cancellationToken).ConfigureAwait(false);
            }
            int probeLength = Math.Max(ImageProbeLength, containers.Select(factory => factory.ProbeLength).DefaultIfEmpty().Max());
            if (probeLength > options.MaxProbeBytes)
            {
                throw new NotSupportedException("Registered media recognition exceeds the configured probe byte limit.");
            }
            MediaBufferAllocator allocator = new(options.BufferBudgetBytes);
            using WritableMediaBuffer writable = allocator.Allocate(probeLength);
            int read = await MediaInputProbe.ReadAsync(source, writable.Memory, cancellationToken).ConfigureAwait(false);
            IImageDecoder? decoder = FindImageDecoder(writable.Memory.Span[..read], source.Length ?? (read < writable.Memory.Length ? read : null));
            using MediaBuffer prefix = writable.Publish();
            await using IByteSource replay = MediaInputProbe.Replay(source, prefix, read);
            IContainerReaderFactory? factory = decoder is null ? FindContainer(prefix.Memory.Span[..read]) : null;
            if (factory is not null)
            {
                await using IContainerReader container = await factory.OpenAsync(replay, allocator, cancellationToken).ConfigureAwait(false);
                return await ReadContainerInfoAsync(container, allocator, options.MaxProbeBytes, cancellationToken).ConfigureAwait(false);
            }
            using MediaBuffer input = await EncodedImageInput.ReadAsync(replay, options.MaxImageInputBytes, allocator, cancellationToken).ConfigureAwait(false);
            decoder ??= FindImageDecoder(input.Memory.Span[..Math.Min(ImageProbeLength, input.Memory.Length)], input.Memory.Length)
                ?? throw new NotSupportedException("No registered media reader recognizes the input.");
            if (decoder is not IImageInfoReader reader)
            {
                throw new NotSupportedException($"Image decoder '{decoder.ImplementationId}' does not expose image metadata.");
            }
            ImageInfo image = await reader.ReadInfoAsync(input.Memory, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(image);
            return new MediaInfo(image);
        }
    }
}
