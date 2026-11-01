// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    /// <summary>Reads bounded metadata and verifies its master CRCs with reusable storage.
    /// Limits accumulate over this reader's lifetime. Borrows the source; operations and disposal are sequential.</summary>
    internal class MatroskaMetadataReader : IDisposable
    {
        private const int ScratchBytes = 4096;
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly WritableMediaBuffer storage;
        private readonly EbmlElementReader elements;
        private readonly MatroskaMetadataLimits limits;
        private int textBytes;
        private int elementCount;
        private int trackCount;
        private bool disposed;

        public MatroskaMetadataReader(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            MatroskaMetadataLimits limits, int maximumSizeWidth = 8)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            limits.Validate();
            this.source = source;
            this.allocator = allocator;
            this.limits = limits;
            storage = allocator.Allocate(ScratchBytes);
            bool created = false;
            try
            {
                elements = new(source, allocator, maximumSizeWidth: maximumSizeWidth);
                created = true;
            }
            finally
            {
                if (!created)
                {
                    storage.Dispose();
                }
            }
        }

        public void ValidateMaster(EbmlElement master, uint expectedId)
        {
            ValidateExtent(master);
            if (master.Id != expectedId)
            {
                throw new ArgumentException("An unexpected Matroska metadata master was supplied.", nameof(master));
            }
        }

        public async ValueTask<EbmlElement?> ReadChildAsync(EbmlElement parent, long offset, CancellationToken token)
        {
            ValidateExtent(parent);
            ArgumentOutOfRangeException.ThrowIfLessThan(offset, parent.DataOffset);
            EbmlElement? next = await elements.ReadAsync(offset, parent.EndOffset, token).ConfigureAwait(false);
            if (next is not EbmlElement child)
            {
                return null;
            }
            if (elementCount == limits.MaximumElements)
            {
                throw new NotSupportedException("Matroska metadata exceeds the configured element count.");
            }
            elementCount++;
            ValidateExtent(child);
            if (child.Id == 0xBF)
            {
                if (offset != parent.DataOffset || child.DataSize != sizeof(uint))
                {
                    throw new InvalidDataException("A metadata CRC-32 must be first and contain four bytes.");
                }
                await VerifyCrcAsync(child, parent.EndOffset!.Value, token).ConfigureAwait(false);
            }
            return child;
        }

        public void ReserveTrack()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (trackCount == limits.MaximumTracks)
            {
                throw new NotSupportedException("Matroska metadata exceeds the configured track count.");
            }
            trackCount++;
        }

        public async ValueTask<ulong> ReadUnsignedAsync(EbmlElement element, ulong defaultValue, CancellationToken token)
        {
            int count = await ReadScalarAsync(element, token).ConfigureAwait(false);
            return EbmlInteger.ReadUnsigned(storage.Memory.Span[..count], defaultValue);
        }

        public async ValueTask<double> ReadPositiveFloatAsync(EbmlElement element, double defaultValue, CancellationToken token)
        {
            int count = await ReadScalarAsync(element, token).ConfigureAwait(false);
            double value = EbmlValue.ReadFloat(storage.Memory.Span[..count], defaultValue);
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new InvalidDataException("A Matroska metadata floating-point value must be finite and positive.");
            }
            return value;
        }

        public async ValueTask<long> ReadDateAsync(EbmlElement element, CancellationToken token)
        {
            ValidateExtent(element);
            if (element.DataSize is not (0 or sizeof(long)))
            {
                throw new InvalidDataException("An EBML date must contain zero or eight bytes.");
            }
            int count = await ReadScalarAsync(element, token).ConfigureAwait(false);
            return EbmlInteger.ReadSigned(storage.Memory.Span[..count], 0);
        }

        public async ValueTask<bool> ReadFlagAsync(EbmlElement element, bool defaultValue, CancellationToken token)
        {
            ulong value = await ReadUnsignedAsync(element, defaultValue ? 1UL : 0UL, token).ConfigureAwait(false);
            return value switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("A Matroska flag must be zero or one.")
            };
        }

        public async ValueTask<Guid> ReadUuidAsync(EbmlElement element, CancellationToken token)
        {
            ValidateExtent(element);
            if (element.DataSize != 16)
            {
                throw new InvalidDataException("A Matroska Segment UUID must contain sixteen bytes.");
            }
            await source.ReadExactlyAtAsync(element.DataOffset, storage.Memory[..16], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Guid value = new(storage.Memory.Span[..16], bigEndian: true);
            if (value == Guid.Empty)
            {
                throw new InvalidDataException("A Matroska Segment UUID must be nonzero.");
            }
            return value;
        }

        public async ValueTask<string> ReadTextAsync(EbmlElement element, bool utf8, CancellationToken token, string defaultValue = "")
        {
            ValidateExtent(element);
            token.ThrowIfCancellationRequested();
            long size = element.DataSize!.Value;
            if (size > limits.MaximumTextBytes - textBytes)
            {
                throw new NotSupportedException("Matroska metadata exceeds the configured text byte limit.");
            }
            int count = (int)size;
            textBytes += count;
            if (count == 0)
            {
                return defaultValue;
            }
            if (count <= ScratchBytes)
            {
                await source.ReadExactlyAtAsync(element.DataOffset, storage.Memory[..count], token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return DecodeText(storage.Memory.Span[..count], utf8);
            }
            using WritableMediaBuffer text = allocator.Allocate(count);
            await source.ReadExactlyAtAsync(element.DataOffset, text.Memory, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return DecodeText(text.Memory.Span, utf8);
        }

        private static string DecodeText(ReadOnlySpan<byte> data, bool utf8)
            => utf8 ? EbmlValue.ReadUtf8(data) : EbmlValue.ReadString(data);

        private async ValueTask<int> ReadScalarAsync(EbmlElement element, CancellationToken token)
        {
            ValidateExtent(element);
            token.ThrowIfCancellationRequested();
            if (element.DataSize > sizeof(ulong))
            {
                throw new InvalidDataException("A Matroska metadata scalar exceeds eight bytes.");
            }
            int count = (int)element.DataSize!.Value;
            await source.ReadExactlyAtAsync(element.DataOffset, storage.Memory[..count], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return count;
        }

        private async ValueTask VerifyCrcAsync(EbmlElement crc, long end, CancellationToken token)
        {
            await source.ReadExactlyAtAsync(crc.DataOffset, storage.Memory[..sizeof(uint)], token).ConfigureAwait(false);
            uint expected = BinaryPrimitives.ReadUInt32LittleEndian(storage.Memory.Span);
            uint actual = 0;
            long position = crc.EndOffset!.Value;
            while (position < end)
            {
                int count = (int)Math.Min(ScratchBytes, end - position);
                await source.ReadExactlyAtAsync(position, storage.Memory[..count], token).ConfigureAwait(false);
                actual = IeeeCrc32.Append(actual, storage.Memory.Span[..count], token);
                position += count;
            }
            token.ThrowIfCancellationRequested();
            if (actual != expected)
            {
                throw new InvalidDataException("The Matroska metadata CRC-32 does not match its payload.");
            }
        }

        private void ValidateExtent(EbmlElement element)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (element.DataOffset < 0 || element.EndOffset is not long end || end < element.DataOffset ||
                (source.Length is long length && end > length))
            {
                throw new InvalidDataException("Matroska metadata must have a bounded extent within its source.");
            }
        }

        public void Dispose()
        {
            disposed = true;
            elements.Dispose();
            storage.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
