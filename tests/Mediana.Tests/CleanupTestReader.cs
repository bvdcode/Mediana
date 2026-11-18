// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;

namespace Mediana.Tests
{
    public class CleanupTestReader(IReadOnlyList<EncodedStreamInfo> streams, bool throws = false) : IContainerReader
    {
        public ContainerInfo Info { get; } = new("test");
        public IReadOnlyList<EncodedStreamInfo> Streams => streams;
        public ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<MediaPacket?>(null);
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (throws) { throw new IOException("Reader disposal failed"); }
            return ValueTask.CompletedTask;
        }
    }
}
