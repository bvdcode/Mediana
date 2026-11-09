// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class FixturePacketContainerFactory(IContainerReaderFactory factory) : IContainerReaderFactory
    {
        public string ImplementationId => factory.ImplementationId;
        public int ProbeLength => factory.ProbeLength;
        public bool CanRead(ReadOnlySpan<byte> prefix) => factory.CanRead(prefix);
        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) => new FixturePacketContainer(
                (IIndexedPacketPrefixReader)await factory.OpenAsync(source, allocator, cancellationToken));
    }
}
