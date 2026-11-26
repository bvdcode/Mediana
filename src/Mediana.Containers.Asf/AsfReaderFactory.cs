// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Asf
{
    /// <summary>Recognizes ASF and its WMV/WMA variants by the Header object's identifier.</summary>
    public class AsfReaderFactory : IContainerReaderFactory
    {
        public string ImplementationId => "asf.managed";
        public int ProbeLength => 16;
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length >= ProbeLength && new Guid(prefix[..16]) == AsfObjects.Header;

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (source is not IRandomAccessByteSource { Length: not null } random)
            {
                throw new NotSupportedException("ASF demultiplexing requires a known-length random access source.");
            }
            AsfLayout layout = new(random, allocator);
            await layout.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new AsfReader(random, allocator, layout);
        }
    }
}
