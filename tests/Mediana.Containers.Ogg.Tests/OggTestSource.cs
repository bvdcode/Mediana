// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Containers.Ogg.Tests
{
    internal class OggTestSource(byte[] data, int maximumRead = int.MaxValue) : IRandomAccessByteSource
    {
        public long? Length => data.Length;
        public int Position { get; private set; }
        public bool Disposed { get; private set; }
        public long BytesRead { get; private set; }
        public Action? AfterRead { get; set; }
        public Func<ValueTask>? AfterReadAsync { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            int length = await ReadAtAsync(Position, destination, cancellationToken);
            Position += length;
            return length;
        }

        public async ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            if (Gate is not null)
            {
                Entered.TrySetResult();
                await Gate.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            int start = (int)Math.Min(offset, data.Length);
            int length = Math.Min(data.Length - start, Math.Min(destination.Length, maximumRead));
            data.AsMemory(start, length).CopyTo(destination);
            BytesRead += length;
            AfterRead?.Invoke();
            if (AfterReadAsync is not null) { await AfterReadAsync(); }
            return length;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
