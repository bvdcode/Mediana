// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Ogg
{
    internal class OggPageReader(IByteSource source, MediaBufferAllocator allocator, OggReadOptions options) : IDisposable
    {
        private readonly WritableMediaBuffer header = allocator.Allocate(OggPage.FixedHeaderBytes + byte.MaxValue);
        private long position;

        public async ValueTask<OggPage?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int first = await source.ReadAsync(header.Memory[..1], cancellationToken).ConfigureAwait(false);
            if (first == 0) { return null; }
            if (first != 1) { throw new InvalidDataException("The source returned an invalid Ogg read length."); }
            await source.ReadExactlyAsync(header.Memory.Slice(1, OggPage.FixedHeaderBytes - 1), cancellationToken).ConfigureAwait(false);
            ValidateHeader(header.Span);
            int count = header.Span[26];
            await source.ReadExactlyAsync(header.Memory.Slice(OggPage.FixedHeaderBytes, count), cancellationToken).ConfigureAwait(false);
            int headerBytes = OggPage.FixedHeaderBytes + count;
            int bodyBytes = 0;
            foreach (byte lace in header.Span.Slice(OggPage.FixedHeaderBytes, count)) { bodyBytes += lace; }
            int size = headerBytes + bodyBytes;
            if (source.Length is long length && size > length - position)
            {
                throw new InvalidDataException("An Ogg page extends past the source.");
            }
            using WritableMediaBuffer storage = allocator.Allocate(size);
            header.Memory[..headerBytes].CopyTo(storage.Memory);
            await source.ReadExactlyAsync(storage.Memory[headerBytes..], cancellationToken).ConfigureAwait(false);
            if (options.VerifyChecksums && OggCrc.Compute(storage.Span) != BinaryPrimitives.ReadUInt32LittleEndian(storage.Span[22..]))
            {
                throw new InvalidDataException("An Ogg page checksum does not match its contents.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            OggPage page = new(storage.Publish(), position);
            position = checked(position + size);
            return page;
        }

        private static void ValidateHeader(ReadOnlySpan<byte> data)
        {
            if (!data[..4].SequenceEqual("OggS"u8)) { throw new InvalidDataException("The Ogg page capture pattern is invalid."); }
            if (data[4] != 0) { throw new NotSupportedException("The Ogg bitstream version is not supported."); }
            if ((data[5] & ~7) != 0) { throw new InvalidDataException("An Ogg page uses reserved header flags."); }
            if (BinaryPrimitives.ReadInt64LittleEndian(data[6..]) < -1)
            {
                throw new InvalidDataException("An Ogg page has an invalid granule position.");
            }
        }

        public void Dispose() => header.Dispose();
    }
}
