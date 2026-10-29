// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Sample
{
    internal class GeneratedPacketSource(MediaBufferAllocator allocator) : IContainerReader
    {
        private bool emitted;
        private bool disposed;
        public IReadOnlyList<EncodedStreamInfo> Streams { get; } =
            [new EncodedStreamInfo(0, MediaKind.Video, "sample.rgba", new TimeBase(1, 1))];

        public ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (emitted)
            {
                return ValueTask.FromResult<MediaPacket?>(null);
            }
            emitted = true;
            using WritableMediaBuffer writable = allocator.Allocate(16);
            byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
            pixels.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return ValueTask.FromResult<MediaPacket?>(new MediaPacket(0, buffer, new MediaTimestamp(0, new TimeBase(1, 1))));
        }

        public ValueTask DisposeAsync()
        {
            disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
