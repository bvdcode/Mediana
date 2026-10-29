// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    internal class ShortReadSource(byte[] data) : IByteSource
    {
        private int offset;
        public long? Length => null;
        public bool IsDisposed { get; private set; }

        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (offset == data.Length || destination.IsEmpty)
            {
                return ValueTask.FromResult(0);
            }
            destination.Span[0] = data[offset++];
            return ValueTask.FromResult(1);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
