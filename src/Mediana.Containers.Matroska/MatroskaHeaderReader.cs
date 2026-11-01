// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaHeaderReader
    {
        public static async ValueTask<MatroskaHeader> ReadAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, int maximumHeaderBytes, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeaderBytes);
            EbmlElement header;
            using (EbmlElementReader reader = new(source, allocator))
            {
                header = await reader.ReadAsync(0, source.Length, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The Matroska EBML document header is missing.");
            }
            if (header.Id != 0x1A45DFA3 || header.EndOffset is not long bodyOffset || header.DataSize is not long length || length == 0)
            {
                throw new InvalidDataException("A Matroska file must start with a bounded EBML document header.");
            }
            if (bodyOffset > maximumHeaderBytes)
            {
                throw new NotSupportedException("The EBML document header exceeds the configured byte limit.");
            }
            using WritableMediaBuffer storage = allocator.Allocate((int)length);
            await source.ReadExactlyAtAsync(header.DataOffset, storage.Memory, cancellationToken).ConfigureAwait(false);
            return Parse(storage.Memory.Span, bodyOffset, cancellationToken);
        }

        private static MatroskaHeader Parse(ReadOnlySpan<byte> payload, long bodyOffset, CancellationToken token)
        {
            MatroskaHeaderFields fields = new();
            int offset = 0;
            while (offset < payload.Length)
            {
                token.ThrowIfCancellationRequested();
                EbmlElement child = EbmlElement.ReadHeader(payload[offset..], offset, payload.Length);
                if (child.EndOffset is not long end)
                {
                    throw new InvalidDataException("EBML document-header children must have known lengths.");
                }
                ReadOnlySpan<byte> data = payload.Slice((int)child.DataOffset, (int)(end - child.DataOffset));
                switch (child.Id)
                {
                    case 0xEC:
                        break;
                    case 0xBF:
                        if (offset != 0 || data.Length != sizeof(uint) ||
                            BinaryPrimitives.ReadUInt32LittleEndian(data) != IeeeCrc32.Compute(payload[(int)end..], token))
                        {
                            throw new InvalidDataException("The EBML header CRC-32 is invalid or misplaced.");
                        }
                        break;
                    default:
                        fields.Read(child.Id, data);
                        break;
                }
                offset = (int)end;
            }
            token.ThrowIfCancellationRequested();
            return fields.Complete(bodyOffset);
        }
    }
}
