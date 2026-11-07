// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    public class AviReaderFactory : IContainerReaderFactory
    {
        public string ImplementationId => "avi.managed";
        public int ProbeLength => 12;
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length >= ProbeLength
            && prefix[..4].SequenceEqual("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("AVI "u8);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source is not IRandomAccessByteSource random)
            {
                throw new NotSupportedException("AVI indexing requires a known-length random access source.");
            }
            return await AviReader.OpenAsync(random, allocator, cancellationToken).ConfigureAwait(false);
        }
    }
}
