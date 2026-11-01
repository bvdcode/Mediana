// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Matroska
{
    internal class MatroskaIndexBudget
    {
        private readonly MediaBufferAllocator allocator;
        private readonly int maximumBytes;
        private int allocated;

        public MatroskaIndexBudget(MediaBufferAllocator allocator, int maximumBytes)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
            this.allocator = allocator;
            this.maximumBytes = maximumBytes;
        }

        public int RemainingBytes => maximumBytes - allocated;

        public WritableMediaBuffer Allocate(int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
            if (length > RemainingBytes)
            {
                throw new NotSupportedException("The Matroska packet index exceeds its configured byte limit.");
            }
            WritableMediaBuffer buffer = allocator.Allocate(length);
            allocated += length;
            return buffer;
        }
    }
}
