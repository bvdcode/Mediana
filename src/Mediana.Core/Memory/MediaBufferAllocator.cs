// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Memory
{
    /// <summary>Thread-safe budget for live byte arrays. Object overhead is not included.</summary>
    public class MediaBufferAllocator
    {
        private long allocatedBytes;
        private readonly MediaBufferAllocator? parent;

        public MediaBufferAllocator(long limitBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitBytes);
            LimitBytes = limitBytes;
        }

        public long LimitBytes { get; }
        public long AllocatedBytes => Interlocked.Read(ref allocatedBytes);

        private MediaBufferAllocator(long limitBytes, MediaBufferAllocator parent) : this(limitBytes)
        {
            this.parent = parent;
        }

        /// <summary>Creates a local budget whose allocations also count against this budget and its ancestors.</summary>
        public MediaBufferAllocator CreateScope(long limitBytes) => new(limitBytes, this);

        /// <summary>Tests whether every allocation from this scope counts against the supplied budget.</summary>
        public bool IsWithin(MediaBufferAllocator budget)
        {
            ArgumentNullException.ThrowIfNull(budget);
            for (MediaBufferAllocator? current = this; current is not null; current = current.parent)
            {
                if (ReferenceEquals(current, budget)) { return true; }
            }
            return false;
        }

        public WritableMediaBuffer Allocate(int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
            Reserve(length);
            bool created = false;
            try
            {
                WritableMediaBuffer buffer = new(new SharedMediaBuffer(new byte[length], this));
                created = true;
                return buffer;
            }
            finally
            {
                if (!created) { Release(length); }
            }
        }

        private void Reserve(int length)
        {
            while (true)
            {
                long current = AllocatedBytes;
                if (length > LimitBytes - current)
                {
                    throw new MediaMemoryLimitException(length, LimitBytes);
                }
                if (Interlocked.CompareExchange(ref allocatedBytes, current + length, current) == current)
                {
                    break;
                }
            }
            bool reserved = false;
            try
            {
                parent?.Reserve(length);
                reserved = true;
            }
            finally
            {
                if (!reserved) { Interlocked.Add(ref allocatedBytes, -length); }
            }
        }

        internal void Release(int length)
        {
            Interlocked.Add(ref allocatedBytes, -length);
            parent?.Release(length);
        }
    }
}
