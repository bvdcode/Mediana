// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Memory
{
    /// <summary>Owns temporary allocations with one common lifetime, borrowing the allocator.
    /// Returned memory is valid until disposal. Operations must not overlap.</summary>
    public class MediaBufferWorkspace(MediaBufferAllocator allocator) : IDisposable
    {
        private readonly MediaBufferAllocator owner = allocator ?? throw new ArgumentNullException(nameof(allocator));
        private readonly List<WritableMediaBuffer> buffers = [];
        private bool disposed;

        public Memory<byte> Allocate(int bytes)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            WritableMediaBuffer buffer = owner.Allocate(bytes);
            bool registered = false;
            try
            {
                buffers.Add(buffer);
                registered = true;
                return buffer.Memory;
            }
            finally { if (!registered) { buffer.Dispose(); } }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            foreach (WritableMediaBuffer buffer in buffers) { buffer.Dispose(); }
            buffers.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
