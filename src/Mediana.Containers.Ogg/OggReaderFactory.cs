// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Ogg
{
    public class OggReaderFactory : IContainerReaderFactory
    {
        private readonly OggReadOptions options;

        public OggReaderFactory(OggReadOptions? options = null)
        {
            this.options = options ?? new();
            this.options.Validate();
        }

        public string ImplementationId => "ogg.managed";
        public int ProbeLength => 4;
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.StartsWith("OggS"u8);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source is not IRandomAccessByteSource random)
            {
                throw new NotSupportedException("Indexed Ogg reading requires a random-access source.");
            }
            return await OggReader.OpenAsync(random, allocator, options, cancellationToken).ConfigureAwait(false);
        }
    }
}
