// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Containers.Avi.Tests
{
    internal class AviTestSource(byte[] bytes, int maximumRead = int.MaxValue, Action<long>? afterRead = null) : IRandomAccessByteSource
    {
        public long? Length => bytes.Length;
        public long ReadBytes { get; private set; }
        public bool Disposed { get; private set; }
        private int cursor;

        public ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            int count = (int)Math.Min(Math.Min(destination.Length, maximumRead), bytes.Length - offset);
            bytes.AsMemory((int)offset, count).CopyTo(destination);
            ReadBytes += count;
            afterRead?.Invoke(ReadBytes);
            return ValueTask.FromResult(count);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            int count = await ReadAtAsync(cursor, destination, cancellationToken);
            cursor += count;
            return count;
        }

        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
