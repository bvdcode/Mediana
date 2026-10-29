// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Memory
{
    /// <summary>Thread-safe budget for live byte arrays. Object overhead is not included.</summary>
    public class MediaBufferAllocator
    {
        private long allocatedBytes;

        public MediaBufferAllocator(long limitBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitBytes);
            LimitBytes = limitBytes;
        }

        public long LimitBytes { get; }
        public long AllocatedBytes => Interlocked.Read(ref allocatedBytes);

        public WritableMediaBuffer Allocate(int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
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
            bool created = false;
            try
            {
                WritableMediaBuffer buffer = new(new SharedMediaBuffer(new byte[length], this));
                created = true;
                return buffer;
            }
            finally
            {
                if (!created)
                {
                    Release(length);
                }
            }
        }

        internal void Release(int length) => Interlocked.Add(ref allocatedBytes, -length);
    }
}
