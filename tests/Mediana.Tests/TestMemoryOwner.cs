// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;

namespace Mediana.Tests
{
    internal class TestMemoryOwner(Memory<byte> memory, Action release) : IMemoryOwner<byte>
    {
        private Action? dispose = release;
        public Memory<byte> Memory => memory;
        public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
    }
}
