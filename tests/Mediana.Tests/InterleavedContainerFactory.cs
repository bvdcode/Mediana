// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class InterleavedContainerFactory : IContainerReaderFactory
    {
        public string ImplementationId => "fixture.interleaved";
        public int ProbeLength => 1;
        public int Opens { get; private set; }
        public InterleavedContainer? Reader { get; private set; }
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length != 0;
        public ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Opens++;
            Reader = new(source, allocator);
            return ValueTask.FromResult<IContainerReader>(Reader);
        }
    }
}
