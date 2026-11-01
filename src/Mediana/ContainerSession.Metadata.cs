// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;

namespace Mediana
{
    public partial class ContainerSession
    {
        /// <summary>Picture declarations in container order. Null means the reader does not expose pictures;
        /// an empty list means no pictures were found in its supported inspected metadata.</summary>
        public IReadOnlyList<ContainerPictureInfo>? Pictures => (Reader as IContainerPictureReader)?.Pictures;

        /// <summary>Attached files in storage order. Null means the reader does not expose attachments;
        /// an empty list means none were found. Attachments are independent of Pictures and Streams.</summary>
        public IReadOnlyList<ContainerAttachmentInfo>? Attachments => (Reader as IContainerAttachmentReader)?.Attachments;

        /// <summary>Reads original picture/reference bytes without advancing packets or selecting a cover.
        /// Returned storage belongs to the caller and survives session disposal. Cancellation or a reader
        /// failure faults the session; an unsupported capability or invalid index leaves it usable.</summary>
        public ValueTask<MediaBuffer> ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken = default)
        {
            IContainerPictureReader? pictures = Reader as IContainerPictureReader;
            Func<int, CancellationToken, ValueTask<MediaBuffer>>? read = pictures is null ? null : pictures.ReadPictureAsync;
            return ReadDataAsync(pictureIndex, pictures?.Pictures.Count, read, "pictures", cancellationToken);
        }

        /// <summary>Reads an original attachment without advancing packets or interpreting its contents.
        /// Ownership and failure behavior are the same as ReadPictureAsync.</summary>
        public ValueTask<MediaBuffer> ReadAttachmentAsync(int attachmentIndex, CancellationToken cancellationToken = default)
        {
            IContainerAttachmentReader? attachments = Reader as IContainerAttachmentReader;
            Func<int, CancellationToken, ValueTask<MediaBuffer>>? read = attachments is null ? null : attachments.ReadAttachmentAsync;
            return ReadDataAsync(attachmentIndex, attachments?.Attachments.Count, read, "attachments", cancellationToken);
        }

        /// <summary>Copies an original picture prefix into caller-owned memory without advancing packets.
        /// Session lifetime, single-consumer and failure rules match ReadPictureAsync.</summary>
        public ValueTask<int> ReadPicturePrefixAsync(int pictureIndex, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            IContainerPictureReader? pictures = Reader as IContainerPictureReader;
            Func<int, CancellationToken, ValueTask<int>>? read = pictures is null ? null
                : (index, token) => ReadPrefixAsync(pictures.ReadPicturePrefixAsync(index, destination, token),
                    Math.Min(destination.Length, pictures.Pictures[index].ByteLength));
            return ReadDataAsync(pictureIndex, pictures?.Pictures.Count, read, "pictures", cancellationToken);
        }

        /// <summary>Copies an original attachment prefix without reading its complete payload.
        /// Session lifetime, single-consumer and failure rules match ReadAttachmentAsync.</summary>
        public ValueTask<int> ReadAttachmentPrefixAsync(int attachmentIndex, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            IContainerAttachmentReader? attachments = Reader as IContainerAttachmentReader;
            Func<int, CancellationToken, ValueTask<int>>? read = attachments is null ? null
                : (index, token) => ReadPrefixAsync(attachments.ReadAttachmentPrefixAsync(index, destination, token),
                    (int)Math.Min(destination.Length, attachments.Attachments[index].ByteLength));
            return ReadDataAsync(attachmentIndex, attachments?.Attachments.Count, read, "attachments", cancellationToken);
        }

        private static async ValueTask<int> ReadPrefixAsync(ValueTask<int> pending, int expected)
        {
            int count = await pending.ConfigureAwait(false);
            if (count != expected)
            {
                throw new InvalidDataException("The container reader returned an invalid prefix length.");
            }
            return count;
        }
    }
}
