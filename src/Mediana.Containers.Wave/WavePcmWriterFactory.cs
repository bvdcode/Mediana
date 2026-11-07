// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Containers.Wave
{
    public class WavePcmWriterFactory : IAudioFrameWriterFactory
    {
        public string FormatId => "wave";
        public async ValueTask<IAudioFrameWriter> CreateAsync(Stream destination, AudioFormat format,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) =>
            await WavePcmWriter.OpenAsync(destination, format, allocator, cancellationToken).ConfigureAwait(false);
    }
}
