// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class FixturePictureContainerFactory : IContainerReaderFactory
    {
        private readonly FixtureContainerFactory packets = new();
        public string ImplementationId => "fixture.pictures";
        public int ProbeLength => packets.ProbeLength;
        public FixturePictureContainer? LastReader { get; private set; }
        public bool CanRead(ReadOnlySpan<byte> prefix) => packets.CanRead(prefix);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            FixtureContainer reader = (FixtureContainer)await packets.OpenAsync(source, allocator, cancellationToken);
            LastReader = new FixturePictureContainer(reader, allocator);
            return LastReader;
        }
    }
}
