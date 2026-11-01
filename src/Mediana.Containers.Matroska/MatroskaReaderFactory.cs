// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    public class MatroskaReaderFactory : IContainerReaderFactory
    {
        private readonly MatroskaReadOptions options;

        public MatroskaReaderFactory(MatroskaReadOptions? options = null)
        {
            this.options = options ?? new();
            this.options.Validate();
        }

        public string ImplementationId => "matroska.managed";
        public int ProbeLength => 4;
        private static ReadOnlySpan<byte> Signature => [0x1A, 0x45, 0xDF, 0xA3];
        /// <summary>Recognizes the EBML signature. Opening validates the complete document header and Matroska DocType.</summary>
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.StartsWith(Signature);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source is not IRandomAccessByteSource random)
            {
                throw new NotSupportedException("Matroska indexing requires a known-length random access source.");
            }
            return await MatroskaReader.OpenAsync(random, allocator, options, cancellationToken).ConfigureAwait(false);
        }
    }
}
