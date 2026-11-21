// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Containers
{
    public interface IAudioFrameWriterFactory
    {
        string FormatId { get; }
        /// <summary>Selects the writer's PCM layout while retaining rate and channel identities.
        /// Writers that accept the proposed channel order return it unchanged.</summary>
        AudioFormat SelectFormat(AudioFormat proposed)
        {
            ArgumentNullException.ThrowIfNull(proposed);
            return proposed;
        }
        /// <summary>Borrows the destination and allocator. The writer validates the output format and stream capabilities.</summary>
        ValueTask<IAudioFrameWriter> CreateAsync(Stream destination, AudioFormat format, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default);
    }
}
