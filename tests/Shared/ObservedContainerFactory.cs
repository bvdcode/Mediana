// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class ObservedContainerFactory(IContainerReaderFactory inner) : IContainerReaderFactory
    {
        public string ImplementationId => inner.ImplementationId;
        public int ProbeLength => inner.ProbeLength;
        public int Opens { get; private set; }
        public MediaBufferAllocator? Allocator { get; private set; }
        public Action<IContainerReader>? Opened { get; init; }
        public bool CanRead(ReadOnlySpan<byte> prefix) => inner.CanRead(prefix);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            Opens++;
            Allocator = allocator;
            IContainerReader reader = await inner.OpenAsync(source, allocator, cancellationToken);
            Opened?.Invoke(reader);
            return reader;
        }
    }
}
