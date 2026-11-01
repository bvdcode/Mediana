// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    /// <summary>Reads bounded element headers without moving or owning the source.
    /// Operations and disposal must not overlap. Unknown extents require schema-level resolution.</summary>
    internal class EbmlElementReader : IDisposable
    {
        private const int HeaderBytes = 4 + 8;
        private readonly IRandomAccessByteSource source;
        private readonly WritableMediaBuffer storage;
        private readonly int maximumIdWidth;
        private readonly int maximumSizeWidth;
        private bool disposed;

        public EbmlElementReader(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            int maximumIdWidth = 4, int maximumSizeWidth = 8)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumIdWidth, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumIdWidth, 4);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumSizeWidth, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumSizeWidth, 8);
            if (source.Length is < 0)
            {
                throw new ArgumentException("The byte source length cannot be negative.", nameof(source));
            }
            this.source = source;
            this.maximumIdWidth = maximumIdWidth;
            this.maximumSizeWidth = maximumSizeWidth;
            storage = allocator.Allocate(HeaderBytes);
        }

        public async ValueTask<EbmlElement?> ReadAsync(long offset, long? parentEnd,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            cancellationToken.ThrowIfCancellationRequested();
            long? limit = parentEnd ?? source.Length;
            if (limit is long end)
            {
                if (end < offset || (source.Length is long inputEnd && end > inputEnd))
                {
                    throw new InvalidDataException("The EBML element range exceeds its parent or input.");
                }
                if (end == offset)
                {
                    return null;
                }
            }
            int available = (int)Math.Min(HeaderBytes, (limit ?? long.MaxValue) - offset);
            if (available == 0)
            {
                throw new InvalidDataException("The EBML header exceeds the addressable input.");
            }
            Memory<byte> header = storage.Memory[..available];
            int read = 0;
            while (read < header.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = await source.ReadAtAsync(offset + read, header[read..], cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (count < 0 || count > header.Length - read)
                {
                    throw new InvalidDataException("The byte source returned an invalid EBML read length.");
                }
                if (count == 0)
                {
                    break;
                }
                read += count;
            }
            if (read == 0)
            {
                if (limit is not null)
                {
                    throw new EndOfStreamException("The EBML element header is truncated.");
                }
                return null;
            }
            return EbmlElement.ReadHeader(header.Span[..read], offset, limit, maximumIdWidth, maximumSizeWidth);
        }

        public void Dispose()
        {
            disposed = true;
            storage.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
