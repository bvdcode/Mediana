// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class ContainerImageTestFactory(ContainerImageTestEntry[] pictures, ContainerImageTestEntry[] attachments) : IContainerReaderFactory
    {
        public string ImplementationId => "container.images.fixture";
        public int ProbeLength => 4;
        public ContainerImageTestReader? Reader { get; private set; }
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.StartsWith("IMGS"u8);
        public ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reader = new(pictures, attachments, allocator);
            return ValueTask.FromResult<IContainerReader>(Reader);
        }
    }
}
