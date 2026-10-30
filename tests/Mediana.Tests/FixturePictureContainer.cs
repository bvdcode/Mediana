// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal class FixturePictureContainer(FixtureContainer reader, MediaBufferAllocator allocator) : IContainerPictureReader
    {
        public ContainerInfo Info => reader.Info;
        public IReadOnlyList<EncodedStreamInfo> Streams => reader.Streams;
        public IReadOnlyList<ContainerPictureInfo> Pictures { get; } =
            Array.AsReadOnly(new[] { new ContainerPictureInfo(3, PictureDataKind.Embedded, "image/png") });
        public Func<CancellationToken, ValueTask>? BeforePictureRead { get; set; }

        public async ValueTask<MediaBuffer> ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(reader.IsDisposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(pictureIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pictureIndex, Pictures.Count);
            cancellationToken.ThrowIfCancellationRequested();
            if (BeforePictureRead is not null)
            {
                await BeforePictureRead(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using WritableMediaBuffer writable = allocator.Allocate(3);
            new byte[] { 7, 8, 9 }.CopyTo(writable.Memory);
            return writable.Publish();
        }

        public ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
            => reader.ReadPacketAsync(cancellationToken);

        public ValueTask DisposeAsync() => reader.DisposeAsync();
    }
}
