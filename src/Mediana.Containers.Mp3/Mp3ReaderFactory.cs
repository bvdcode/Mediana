// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Mp3
{
    public class Mp3ReaderFactory : IContainerReaderFactory
    {
        private readonly Mp3ReadOptions options;

        public Mp3ReaderFactory(Mp3ReadOptions? options = null)
        {
            this.options = options ?? new Mp3ReadOptions();
            this.options.Validate();
        }

        public string ImplementationId => "mp3.managed";
        public int ProbeLength => 4;
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length >= ProbeLength
            && (prefix[..3].SequenceEqual("ID3"u8) || Mp3FrameHeader.HasLayer3Sync(prefix));

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) =>
            await Mp3Reader.OpenAsync(source, allocator, options, cancellationToken).ConfigureAwait(false);
    }
}
