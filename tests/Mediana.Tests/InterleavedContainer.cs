// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class InterleavedContainer(IByteSource source, MediaBufferAllocator allocator) : IContainerReader
    {
        private int position;
        public int Reads { get; private set; }
        public bool IsDisposed { get; private set; }
        public Func<CancellationToken, Task>? BeforeRead { get; set; }
        public ContainerInfo Info { get; } = new("interleaved");
        public IReadOnlyList<EncodedStreamInfo> Streams { get; } = Array.AsReadOnly(new[]
        {
            new EncodedStreamInfo(3, MediaKind.Video, "fixture.rgba", new("none", "fixture.rgba", EncodedPacketBoundary.AccessUnit), new(1, 1)),
            new EncodedStreamInfo(7, MediaKind.Audio, "pcm", StreamFormats.WavePcm, new(1, 8000),
                PcmConfiguration.WriteWaveFormat(new AudioFormat(8000, AudioSampleFormat.Unsigned8, AudioStorageLayout.Interleaved, "mono")))
        });

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (BeforeRead is not null)
            {
                await BeforeRead(cancellationToken);
            }
            Reads++;
            using WritableMediaBuffer storage = allocator.Allocate(1);
            if (await source.ReadAsync(storage.Memory, cancellationToken) == 0)
            {
                return null;
            }
            EncodedStreamInfo stream = Streams[position % 2];
            long timestamp = position++ / 2;
            using MediaBuffer buffer = storage.Publish();
            return new MediaPacket(stream.StreamIndex, buffer, new(timestamp, stream.TimeBase));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
