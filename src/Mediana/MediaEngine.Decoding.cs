// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Containers;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Starts audio decoding with an already opened reader and its allocation budget.
        /// Ownership of the reader transfers on success; failure leaves it with the caller.
        /// The reader must be positioned before packet reading. Its byte source remains borrowed.</summary>
        public AudioSession OpenAudio(IContainerReader reader, MediaBufferAllocator allocator, int? streamIndex = null,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            ContainerSession container = new(reader, null, allocator, options);
            return new(CreateDecodeSession(container, streamIndex, MediaKind.Audio, audioDecoders, cancellationToken));
        }

        /// <summary>Starts video decoding using an already opened container, without reading its headers or index again.
        /// Transfers ownership on success: the supplied session becomes inactive and its disposal is harmless.
        /// The returned session owns the existing reader/allocator and retains the original packet/frame limits.
        /// Pictures, attachments and header metadata may be inspected before transfer; packet reads must not have started.
        /// Creation failure or cancellation leaves the supplied session active. Calls must not overlap other session operations.</summary>
        public VideoSession OpenVideo(ContainerSession container, int? streamIndex = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(container);
            return container.Transfer(owner => new VideoSession(CreateDecodeSession(owner, streamIndex, MediaKind.Video, decoders, cancellationToken)));
        }

        /// <summary>Starts audio decoding using the existing container reader and allocator.
        /// Transfers ownership on success; failure leaves the supplied session active. Original packet/frame limits apply.
        /// Header/picture/attachment inspection is allowed, but packet reads must not have started. Borrows the byte source.</summary>
        public AudioSession OpenAudio(ContainerSession container, int? streamIndex = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(container);
            return container.Transfer(owner => new AudioSession(CreateDecodeSession(owner, streamIndex, MediaKind.Audio, audioDecoders, cancellationToken)));
        }
    }
}
