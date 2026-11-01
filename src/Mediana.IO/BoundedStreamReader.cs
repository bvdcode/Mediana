// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.IO
{
    /// <summary>Reads a bounded stream into an independently owned exact-length array, leaving the stream open.
    /// Uses fixed-size working segments followed by at most one final copy; working segments reserve at most
    /// the configured limit, plus list/object overhead. The returned array is additional storage.
    /// Reads one extra byte when the limit is reached to distinguish exact EOF from oversized input.</summary>
    public static class BoundedStreamReader
    {
        public static byte[] Read(Stream source, int maximumBytes, CancellationToken cancellationToken = default)
        {
            Validate(source, maximumBytes);
            BoundedByteBuffer buffer = new(maximumBytes);
            Span<byte> overflow = stackalloc byte[1];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Memory<byte> memory = buffer.GetMemory();
                if (memory.IsEmpty)
                {
                    CheckEnd(source.Read(overflow));
                    break;
                }
                int count = source.Read(memory.Span);
                CheckCount(count, memory.Length);
                if (count == 0)
                {
                    break;
                }
                buffer.Advance(count);
            }
            return buffer.Complete(cancellationToken);
        }

        public static async Task<byte[]> ReadAsync(Stream source, int maximumBytes, CancellationToken cancellationToken = default)
        {
            Validate(source, maximumBytes);
            BoundedByteBuffer buffer = new(maximumBytes);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Memory<byte> memory = buffer.GetMemory();
                if (memory.IsEmpty)
                {
                    int extra = await source.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false);
                    CheckEnd(extra);
                    break;
                }
                int count = await source.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                CheckCount(count, memory.Length);
                if (count == 0)
                {
                    break;
                }
                buffer.Advance(count);
            }
            return buffer.Complete(cancellationToken);
        }

        private static void Validate(Stream source, int maximumBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        }

        private static void CheckCount(int count, int available)
        {
            if (count < 0 || count > available)
            {
                throw new InvalidDataException("The stream returned an invalid read length.");
            }
        }

        private static void CheckEnd(int count)
        {
            CheckCount(count, 1);
            if (count != 0)
            {
                throw new InvalidDataException("The stream exceeds the configured byte limit.");
            }
        }
    }
}
