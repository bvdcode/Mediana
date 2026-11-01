// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal class FixturePictureContainer(FixtureContainer reader, MediaBufferAllocator allocator) : IContainerPictureReader, IContainerAttachmentReader
    {
        public ContainerInfo Info => reader.Info;
        public IReadOnlyList<EncodedStreamInfo> Streams => reader.Streams;
        public IReadOnlyList<ContainerPictureInfo> Pictures { get; } =
            Array.AsReadOnly(new[] { new ContainerPictureInfo(3, PictureDataKind.Embedded, "image/png") });
        public Func<CancellationToken, ValueTask>? BeforePictureRead { get; set; }
        public IReadOnlyList<ContainerAttachmentInfo> Attachments { get; } =
            Array.AsReadOnly(new[] { new ContainerAttachmentInfo(3, "cover.png", "image/png", "Front") });
        public Func<CancellationToken, ValueTask>? BeforeAttachmentRead { get; set; }

        public ValueTask<MediaBuffer> ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken = default)
            => ReadDataAsync(pictureIndex, Pictures.Count, BeforePictureRead, cancellationToken);

        public ValueTask<MediaBuffer> ReadAttachmentAsync(int attachmentIndex, CancellationToken cancellationToken = default)
            => ReadDataAsync(attachmentIndex, Attachments.Count, BeforeAttachmentRead, cancellationToken);

        private async ValueTask<MediaBuffer> ReadDataAsync(int index, int count,
            Func<CancellationToken, ValueTask>? beforeRead, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(reader.IsDisposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
            cancellationToken.ThrowIfCancellationRequested();
            if (beforeRead is not null)
            {
                await beforeRead(cancellationToken);
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
