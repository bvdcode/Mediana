// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class FixtureContainer(IByteSource source, MediaBufferAllocator allocator) : IContainerReader
    {
        private int index;
        public bool IsDisposed { get; private set; }
        public IReadOnlyList<EncodedStreamInfo> Streams { get; } =
            [new EncodedStreamInfo(0, MediaKind.Video, "fixture.rgba", new TimeBase(1, 1))];

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            using WritableMediaBuffer writable = allocator.Allocate(1);
            int read = await source.ReadAsync(writable.Memory, cancellationToken);
            if (read == 0)
            {
                return null;
            }
            using MediaBuffer buffer = writable.Publish();
            return new MediaPacket(0, buffer, new MediaTimestamp(index++, new TimeBase(1, 1)));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
