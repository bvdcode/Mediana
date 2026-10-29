// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    internal class TrackingRandomAccessSource(byte[] data, int maxRead = int.MaxValue) : IRandomAccessByteSource
    {
        public long? Length => data.Length;
        public bool IsDisposed { get; private set; }
        public List<(long Offset, int Length)> Reads { get; } = [];
        public Action? AfterRead { get; set; }

        public ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            int count = offset >= data.Length ? 0 : (int)Math.Min(Math.Min(destination.Length, maxRead), data.Length - offset);
            data.AsMemory((int)Math.Min(offset, data.Length), count).CopyTo(destination);
            Reads.Add((offset, count));
            AfterRead?.Invoke();
            return ValueTask.FromResult(count);
        }

        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This fixture requires range reads.");

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
