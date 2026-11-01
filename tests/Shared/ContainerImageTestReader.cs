// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal class ContainerImageTestReader(ContainerImageTestEntry[] pictures, ContainerImageTestEntry[] attachments,
        MediaBufferAllocator allocator) : IContainerPictureReader, IContainerAttachmentReader
    {
        private bool disposed;
        public ContainerInfo Info { get; } = new("fixture");
        public IReadOnlyList<EncodedStreamInfo> Streams => [];
        public IReadOnlyList<ContainerPictureInfo> Pictures { get; } = Array.AsReadOnly(pictures.Select(item =>
            new ContainerPictureInfo(checked((int)item.ByteLength), item.DataKind, item.MediaType)).ToArray());
        public IReadOnlyList<ContainerAttachmentInfo> Attachments { get; } = Array.AsReadOnly(attachments.Select(item =>
            new ContainerAttachmentInfo(item.ByteLength, item.FileName, item.MediaType)).ToArray());
        public List<(ContainerImageSource Source, int Index)> PrefixReads { get; } = [];
        public List<(ContainerImageSource Source, int Index)> PayloadReads { get; } = [];
        public int PacketReads { get; private set; }
        public Func<CancellationToken, ValueTask>? BeforePrefix { get; set; }
        public Func<ValueTask>? AfterPayload { get; set; }

        public ValueTask<int> ReadPicturePrefixAsync(int pictureIndex, Memory<byte> destination, CancellationToken cancellationToken = default) =>
            ReadPrefixAsync(ContainerImageSource.Picture, pictureIndex, destination, cancellationToken);
        public ValueTask<int> ReadAttachmentPrefixAsync(int attachmentIndex, Memory<byte> destination, CancellationToken cancellationToken = default) =>
            ReadPrefixAsync(ContainerImageSource.Attachment, attachmentIndex, destination, cancellationToken);
        public ValueTask<MediaBuffer> ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken = default) =>
            ReadAsync(ContainerImageSource.Picture, pictureIndex, cancellationToken);
        public ValueTask<MediaBuffer> ReadAttachmentAsync(int attachmentIndex, CancellationToken cancellationToken = default) =>
            ReadAsync(ContainerImageSource.Attachment, attachmentIndex, cancellationToken);

        private async ValueTask<int> ReadPrefixAsync(ContainerImageSource source, int index, Memory<byte> destination, CancellationToken token)
        {
            ContainerImageTestEntry entry = Get(source, index);
            token.ThrowIfCancellationRequested();
            PrefixReads.Add((source, index));
            if (BeforePrefix is not null)
            {
                await BeforePrefix(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(destination.Length, entry.ByteLength);
            entry.Data.AsMemory(0, count).CopyTo(destination);
            return count;
        }

        private async ValueTask<MediaBuffer> ReadAsync(ContainerImageSource source, int index, CancellationToken token)
        {
            ContainerImageTestEntry entry = Get(source, index);
            token.ThrowIfCancellationRequested();
            PayloadReads.Add((source, index));
            if (entry.ByteLength != entry.Data.Length)
            {
                throw new InvalidOperationException("A deferred nonimage payload was materialized.");
            }
            using WritableMediaBuffer writable = allocator.Allocate(Math.Max(1, entry.Data.Length));
            entry.Data.CopyTo(writable.Memory);
            if (AfterPayload is not null)
            {
                await AfterPayload().ConfigureAwait(false);
            }
            using MediaBuffer owned = writable.Publish();
            return owned.Slice(0, entry.Data.Length);
        }

        private ContainerImageTestEntry Get(ContainerImageSource source, int index)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ContainerImageTestEntry entry = source switch
            {
                ContainerImageSource.Picture => pictures[index],
                ContainerImageSource.Attachment => attachments[index],
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            };
            if (entry.DataKind == PictureDataKind.ExternalReference)
            {
                throw new InvalidOperationException("An external picture reference was read.");
            }
            return entry;
        }

        public ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            PacketReads++;
            return ValueTask.FromResult<MediaPacket?>(null);
        }

        public ValueTask DisposeAsync()
        {
            disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
