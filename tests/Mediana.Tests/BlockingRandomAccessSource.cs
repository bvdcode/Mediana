// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    internal class BlockingRandomAccessSource(byte[] data) : IRandomAccessByteSource
    {
        private readonly TrackingRandomAccessSource inner = new(data, 13);

        public bool BlockReads { get; set; }
        public long? Length => inner.Length;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            if (BlockReads)
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return await inner.ReadAtAsync(offset, destination, cancellationToken);
        }

        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(destination, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
