// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;

namespace Mediana.Core.Memory
{
    internal class SharedMediaBuffer(Memory<byte> data, MediaBufferAllocator allocator, IMemoryOwner<byte>? owner = null)
    {
        private int references = 1;
        private readonly int capacity = owner?.Memory.Length ?? data.Length;
        public Memory<byte> Data { get; } = data;

        public void Retain()
        {
            int current;
            do
            {
                current = Volatile.Read(ref references);
                ObjectDisposedException.ThrowIf(current == 0, this);
                if (current == int.MaxValue)
                {
                    throw new InvalidOperationException("The buffer reference count is exhausted.");
                }
            }
            while (Interlocked.CompareExchange(ref references, current + 1, current) != current);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref references) == 0)
            {
                try { owner?.Dispose(); }
                finally { allocator.Release(capacity); }
            }
        }
    }
}
