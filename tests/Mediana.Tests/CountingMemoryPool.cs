// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;

namespace Mediana.Tests
{
    internal class CountingMemoryPool : MemoryPool<byte>
    {
        public int Rentals { get; private set; }
        public int Returns { get; private set; }
        public bool Disposed { get; private set; }
        public override int MaxBufferSize => MemoryPool<byte>.Shared.MaxBufferSize;

        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(minBufferSize);
            Rentals++;
            return new TestMemoryOwner(owner.Memory, () => { owner.Dispose(); Returns++; });
        }

        protected override void Dispose(bool disposing) => Disposed = true;
    }
}
