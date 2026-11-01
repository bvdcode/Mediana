// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    /// <summary>Reads Block/SimpleBlock framing and retains the current frame ranges until the next read.
    /// Borrows the source. Operations, range access and disposal must not overlap.</summary>
    internal class MatroskaBlockReader : IDisposable
    {
        private const int CacheBytes = 256;
        private const int FrameEntryBytes = sizeof(long) + sizeof(int);
        private const int MaximumFrames = 256;
        private readonly IRandomAccessByteSource source;
        private readonly WritableMediaBuffer storage;
        private readonly int maximumHeaderBytes;
        private readonly int maximumFrameBytes;
        private long blockStart;
        private long blockEnd;
        private long position;
        private int cacheOffset;
        private int cacheCount;
        private int frameCount;
        private bool ready;
        private bool disposed;

        public MatroskaBlockReader(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            int maximumHeaderBytes, int maximumFrameBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeaderBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrameBytes);
            this.source = source;
            this.maximumHeaderBytes = maximumHeaderBytes;
            this.maximumFrameBytes = maximumFrameBytes;
            storage = allocator.Allocate(CacheBytes + MaximumFrames * FrameEntryBytes);
        }

        public async ValueTask<MatroskaBlockHeader> ReadAsync(EbmlElement block, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ready = false;
            cancellationToken.ThrowIfCancellationRequested();
            bool simple = block.Id switch
            {
                0xA3 => true,
                0xA1 => false,
                _ => throw new ArgumentException("A Matroska Block or SimpleBlock is required.", nameof(block))
            };
            if (block.DataOffset < 0 || block.EndOffset is not long end || end < block.DataOffset ||
                (source.Length is long length && end > length))
            {
                throw new InvalidDataException("The Matroska block extent is invalid.");
            }
            blockStart = block.DataOffset;
            blockEnd = end;
            position = blockStart;
            cacheOffset = 0;
            cacheCount = 0;
            EbmlVint track = await ReadVintAsync(cancellationToken).ConfigureAwait(false);
            if (track.Value == 0)
            {
                throw new InvalidDataException("A Matroska block track number cannot be zero.");
            }
            await EnsureAsync(3, cancellationToken).ConfigureAwait(false);
            short timestamp = BinaryPrimitives.ReadInt16BigEndian(storage.Memory.Span.Slice(cacheOffset, 2));
            byte flags = storage.Memory.Span[cacheOffset + 2];
            Consume(3);
            int reservedMask = simple ? 0x70 : 0xF0;
            if ((flags & reservedMask) != 0)
            {
                throw new InvalidDataException("Reserved Matroska block flag bits must be zero.");
            }
            MatroskaLacingMode lacing = (MatroskaLacingMode)((flags >> 1) & 3);
            frameCount = 1;
            if (lacing != MatroskaLacingMode.None)
            {
                await EnsureAsync(1, cancellationToken).ConfigureAwait(false);
                frameCount = storage.Memory.Span[cacheOffset] + 1;
                Consume(1);
                if (frameCount == 1)
                {
                    throw new InvalidDataException("A Matroska lace must contain at least two frames.");
                }
            }
            long knownBytes = 0;
            switch (lacing)
            {
                case MatroskaLacingMode.None:
                    break;
                case MatroskaLacingMode.FixedSize:
                    long remaining = blockEnd - position;
                    if (remaining % frameCount != 0)
                    {
                        throw new InvalidDataException("A fixed Matroska lace must contain equally sized frames.");
                    }
                    for (int index = 0; index < frameCount - 1; index++)
                    {
                        knownBytes += StoreLength(index, remaining / frameCount);
                    }
                    break;
                case MatroskaLacingMode.Xiph:
                    for (int index = 0; index < frameCount - 1; index++)
                    {
                        knownBytes += StoreLength(index, await ReadXiphSizeAsync(cancellationToken).ConfigureAwait(false));
                    }
                    break;
                case MatroskaLacingMode.Ebml:
                    long previous = (long)(await ReadVintAsync(cancellationToken).ConfigureAwait(false)).Value;
                    knownBytes += StoreLength(0, previous);
                    for (int index = 1; index < frameCount - 1; index++)
                    {
                        EbmlVint difference = await ReadVintAsync(cancellationToken).ConfigureAwait(false);
                        long bias = (1L << (7 * difference.Width - 1)) - 1;
                        previous += (long)difference.Value - bias;
                        knownBytes += StoreLength(index, previous);
                    }
                    break;
                default:
                    throw new InvalidDataException("The Matroska lacing mode is invalid.");
            }
            StoreLength(frameCount - 1, blockEnd - position - knownBytes);
            long frameOffset = position;
            for (int index = 0; index < frameCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int entryOffset = CacheBytes + index * FrameEntryBytes;
                BinaryPrimitives.WriteInt64BigEndian(storage.Memory.Span.Slice(entryOffset, sizeof(long)), frameOffset);
                frameOffset += BinaryPrimitives.ReadInt32BigEndian(storage.Memory.Span.Slice(entryOffset + sizeof(long), sizeof(int)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ready = true;
            return new(track.Value, timestamp, flags, lacing, frameCount, position, simple);
        }

        public MatroskaFrameRange GetFrame(int index)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!ready)
            {
                throw new InvalidOperationException("No complete Matroska block layout is available.");
            }
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, frameCount);
            int offset = CacheBytes + index * FrameEntryBytes;
            ReadOnlySpan<byte> entry = storage.Memory.Span.Slice(offset, FrameEntryBytes);
            return new(BinaryPrimitives.ReadInt64BigEndian(entry), BinaryPrimitives.ReadInt32BigEndian(entry[sizeof(long)..]));
        }

        private int StoreLength(int index, long length)
        {
            if (length < 0)
            {
                throw new InvalidDataException("A Matroska lace frame has a negative size or exceeds the block.");
            }
            if (length > maximumFrameBytes)
            {
                throw new NotSupportedException("A Matroska frame exceeds the configured byte limit.");
            }
            BinaryPrimitives.WriteInt32BigEndian(storage.Memory.Span.Slice(CacheBytes + index * FrameEntryBytes + sizeof(long), sizeof(int)), (int)length);
            return (int)length;
        }

        private async ValueTask<EbmlVint> ReadVintAsync(CancellationToken cancellationToken)
        {
            await EnsureAsync(1, cancellationToken).ConfigureAwait(false);
            int width = BitOperations.LeadingZeroCount((uint)storage.Memory.Span[cacheOffset]) - 23;
            if (width > 8)
            {
                throw new InvalidDataException("The Matroska block variable integer is invalid.");
            }
            await EnsureAsync(width, cancellationToken).ConfigureAwait(false);
            EbmlVint value = EbmlInteger.ReadVint(storage.Memory.Span.Slice(cacheOffset, width));
            Consume(width);
            return value;
        }

        private async ValueTask<long> ReadXiphSizeAsync(CancellationToken cancellationToken)
        {
            long length = 0;
            while (true)
            {
                await EnsureAsync(1, cancellationToken).ConfigureAwait(false);
                int available = cacheCount - cacheOffset;
                int terminator = storage.Memory.Span.Slice(cacheOffset, available).IndexOfAnyExcept(byte.MaxValue);
                if (terminator >= 0)
                {
                    length += (long)terminator * byte.MaxValue + storage.Memory.Span[cacheOffset + terminator];
                    Consume(terminator + 1);
                    return length;
                }
                length += (long)available * byte.MaxValue;
                Consume(available);
                if (length > maximumFrameBytes)
                {
                    throw new NotSupportedException("A Matroska frame exceeds the configured byte limit.");
                }
            }
        }

        private async ValueTask EnsureAsync(int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count > blockEnd - position)
            {
                throw new EndOfStreamException("The Matroska block header is truncated.");
            }
            long remainingHeader = maximumHeaderBytes - (position - blockStart);
            if (count > remainingHeader)
            {
                throw new NotSupportedException("The Matroska block header exceeds the configured byte limit.");
            }
            int available = cacheCount - cacheOffset;
            if (available >= count)
            {
                return;
            }
            storage.Memory.Span.Slice(cacheOffset, available).CopyTo(storage.Memory.Span);
            int capacity = (int)Math.Min(Math.Min(CacheBytes, blockEnd - position), remainingHeader);
            await source.ReadExactlyAtAsync(position + available, storage.Memory.Slice(available, capacity - available), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            cacheOffset = 0;
            cacheCount = capacity;
        }

        private void Consume(int count)
        {
            cacheOffset += count;
            position += count;
        }

        public void Dispose()
        {
            disposed = true;
            storage.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
