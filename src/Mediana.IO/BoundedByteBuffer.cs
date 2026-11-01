// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.IO
{
    internal class BoundedByteBuffer(int maximumBytes)
    {
        private const int SegmentBytes = 64 * 1024;
        private readonly List<byte[]> segments = [];
        private byte[]? current;
        private int used;
        private int length;

        internal Memory<byte> GetMemory()
        {
            if (length == maximumBytes)
            {
                return Memory<byte>.Empty;
            }
            if (current is null || used == current.Length)
            {
                current = new byte[Math.Min(SegmentBytes, maximumBytes - length)];
                segments.Add(current);
                used = 0;
            }
            return current.AsMemory(used);
        }

        internal void Advance(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (current is null || count > current.Length - used)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            used += count;
            length += count;
        }

        internal byte[] Complete(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (length == 0)
            {
                return [];
            }
            if (segments.Count == 1 && current is not null && used == current.Length)
            {
                return current;
            }
            byte[] result = new byte[length];
            int offset = 0;
            foreach (byte[] segment in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(segment.Length, length - offset);
                segment.AsSpan(0, count).CopyTo(result.AsSpan(offset));
                offset += count;
            }
            return result;
        }
    }
}
