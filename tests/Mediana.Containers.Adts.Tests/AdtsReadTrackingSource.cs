// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    internal class AdtsReadTrackingSource(byte[] data) : IRandomAccessByteSource
    {
        public long? Length => data.Length;
        public int Position { get; private set; }
        public bool IsDisposed { get; private set; }
        public List<(long Offset, int Length)> Reads { get; } = [];

        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            int count = CopyByte(Position, destination, cancellationToken);
            Position += count;
            return ValueTask.FromResult(count);
        }

        public ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CopyByte(offset, destination, cancellationToken));

        private int CopyByte(long offset, Memory<byte> destination, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            token.ThrowIfCancellationRequested();
            if (offset >= data.Length || destination.IsEmpty) { return 0; }
            int count = Math.Min(1, destination.Length);
            data.AsMemory((int)offset, count).CopyTo(destination);
            Reads.Add((offset, count));
            return count;
        }

        public ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }
}
