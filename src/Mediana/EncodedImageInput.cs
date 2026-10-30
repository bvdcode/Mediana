// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana
{
    internal static class EncodedImageInput
    {
        private const int InitialCapacity = 8192;

        public static async ValueTask<MediaBuffer> ReadAsync(IByteSource source, int maximumBytes,
            MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            if (source.Length is < 0)
            {
                throw new InvalidDataException("The image source declared a negative length.");
            }
            if (source.Length > maximumBytes)
            {
                throw new NotSupportedException("The encoded image exceeds the configured input byte limit.");
            }
            int capacity = source.Length is long length ? Math.Max(1, (int)length) : Math.Min(InitialCapacity, maximumBytes);
            WritableMediaBuffer writable = allocator.Allocate(capacity);
            try
            {
                using WritableMediaBuffer extra = allocator.Allocate(1);
                int used = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count;
                    if (used == capacity)
                    {
                        count = await source.ReadAsync(extra.Memory, cancellationToken).ConfigureAwait(false);
                        ValidateCount(count, 1);
                        if (count == 0)
                        {
                            break;
                        }
                        if (used == maximumBytes)
                        {
                            throw new NotSupportedException("The encoded image exceeds the configured input byte limit.");
                        }
                        int expandedCapacity = (int)Math.Min((long)capacity * 2, maximumBytes);
                        WritableMediaBuffer expanded = allocator.Allocate(expandedCapacity);
                        writable.Memory[..used].CopyTo(expanded.Memory);
                        writable.Dispose();
                        writable = expanded;
                        capacity = expandedCapacity;
                        writable.Memory.Span[used++] = extra.Memory.Span[0];
                    }
                    else
                    {
                        count = await source.ReadAsync(writable.Memory[used..], cancellationToken).ConfigureAwait(false);
                        ValidateCount(count, capacity - used);
                        if (count == 0)
                        {
                            break;
                        }
                        used += count;
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                using MediaBuffer complete = writable.Publish();
                return complete.Slice(0, used);
            }
            finally
            {
                writable.Dispose();
            }
        }

        private static void ValidateCount(int count, int requested)
        {
            if (count < 0 || count > requested)
            {
                throw new InvalidDataException("The source returned an invalid read length.");
            }
        }
    }
}
