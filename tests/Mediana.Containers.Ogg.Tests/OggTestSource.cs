// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Containers.Ogg.Tests
{
    internal class OggTestSource(byte[] data, int maximumRead = int.MaxValue) : IByteSource
    {
        public long? Length => data.Length;
        public int Position { get; private set; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            if (Gate is not null)
            {
                Entered.TrySetResult();
                await Gate.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(data.Length - Position, Math.Min(destination.Length, maximumRead));
            data.AsMemory(Position, length).CopyTo(destination);
            Position += length;
            return length;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
