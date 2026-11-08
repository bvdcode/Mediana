// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class FixtureContainer(IByteSource source, MediaBufferAllocator allocator, ContainerInfo? info = null,
        Func<int, StreamMetadata?>? metadata = null) : IContainerReader, IContainerStreamMetadataProvider
    {
        private int index;
        public ContainerInfo Info { get; } = info ?? new("fixture");
        public int PacketReads { get; private set; }
        public int MetadataReads { get; private set; }
        public bool IsDisposed { get; private set; }
        public IReadOnlyList<EncodedStreamInfo> Streams { get; } =
            [new EncodedStreamInfo(0, MediaKind.Video, "fixture.rgba", new EncodedStreamFormat("none", "fixture.rgba", EncodedPacketBoundary.AccessUnit), new TimeBase(1, 1))];

        public StreamMetadata? GetStreamMetadata(int streamIndex)
        {
            if (streamIndex != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(streamIndex));
            }
            MetadataReads++;
            return metadata?.Invoke(streamIndex);
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            PacketReads++;
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
