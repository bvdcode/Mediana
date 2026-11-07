// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Opens video using the caller's shared storage budget and optional pool for container and codec allocations.
        /// A null stream index selects the first video stream. Borrows source and allocator; frames own independent leases.</summary>
        public async ValueTask<VideoSession> OpenVideoAsync(IByteSource source, MediaBufferAllocator allocator,
            MediaOpenOptions options, int? streamIndex, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(options);
            return new(await OpenCoreAsync(source, streamIndex, MediaKind.Video, decoders, options, cancellationToken, allocator).ConfigureAwait(false));
        }

        /// <summary>Opens audio using the caller's shared storage budget and optional pool for container and codec allocations.
        /// A null stream index selects the first audio stream. Borrows source and allocator; frames own independent leases.</summary>
        public async ValueTask<AudioSession> OpenAudioAsync(IByteSource source, MediaBufferAllocator allocator,
            MediaOpenOptions options, int? streamIndex, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(options);
            return new(await OpenCoreAsync(source, streamIndex, MediaKind.Audio, audioDecoders, options, cancellationToken, allocator).ConfigureAwait(false));
        }
    }
}
