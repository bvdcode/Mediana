// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mediana.Models.Mesh
{
    internal class MeshBuffer<T> : IDisposable where T : unmanaged
    {
        private readonly WritableMediaBuffer _storage;
        internal Span<T> Span => MemoryMarshal.Cast<byte, T>(_storage.Span);

        internal MeshBuffer(int length, MediaBufferAllocator allocator)
        {
            _storage = allocator.Allocate(checked(length * Unsafe.SizeOf<T>()));
        }

        public void Dispose() => _storage.Dispose();
    }
}
