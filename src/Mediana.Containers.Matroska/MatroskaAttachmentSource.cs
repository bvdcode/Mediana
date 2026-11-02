// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    internal class MatroskaAttachmentSource(IRandomAccessByteSource source, MediaBufferAllocator allocator,
        IReadOnlyList<MatroskaAttachmentInfo> attachments, int maximumBytes)
    {
        public IReadOnlyList<MatroskaAttachmentInfo> Attachments => attachments;

        public async ValueTask<int> ReadPrefixAsync(int index, Memory<byte> destination, CancellationToken token)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, attachments.Count);
            token.ThrowIfCancellationRequested();
            MatroskaAttachmentInfo attachment = attachments[index];
            int length = (int)Math.Min(destination.Length, attachment.ByteLength);
            if (length > maximumBytes)
            {
                throw new NotSupportedException("The Matroska attachment prefix exceeds the configured read byte limit.");
            }
            await source.ReadExactlyAtAsync(attachment.Data.DataOffset, destination[..length], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return length;
        }

        public async ValueTask<MediaBuffer> ReadAsync(int index, CancellationToken token)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, attachments.Count);
            token.ThrowIfCancellationRequested();
            MatroskaAttachmentInfo attachment = attachments[index];
            if (attachment.ByteLength > maximumBytes)
            {
                throw new NotSupportedException("The Matroska attachment exceeds the configured read byte limit.");
            }
            int length = (int)attachment.ByteLength;
            using WritableMediaBuffer writable = allocator.Allocate(Math.Max(1, length));
            await source.ReadExactlyAtAsync(attachment.Data.DataOffset, writable.Memory[..length], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return buffer.Slice(0, length);
        }
    }
}
