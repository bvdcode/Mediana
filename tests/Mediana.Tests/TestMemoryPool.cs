// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;

namespace Mediana.Tests
{
    internal class TestMemoryPool(int capacity) : MemoryPool<byte>
    {
        private readonly byte[] bytes = Enumerable.Repeat((byte)137, capacity).ToArray();
        private bool rented;
        internal int Returns { get; private set; }
        public override int MaxBufferSize => bytes.Length;
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            if (rented || minBufferSize > bytes.Length) { throw new InvalidOperationException(); }
            rented = true;
            return new TestMemoryOwner(bytes, () => { rented = false; Returns++; });
        }
        protected override void Dispose(bool disposing) { }
    }
}
