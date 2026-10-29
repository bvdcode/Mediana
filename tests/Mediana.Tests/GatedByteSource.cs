using Mediana.IO;

namespace Mediana.Tests
{
    internal class GatedByteSource(byte[] data) : IByteSource
    {
        private int position;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool GateReads { get; set; }
        public bool IsDisposed { get; private set; }
        public long? Length => data.Length;

        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (GateReads)
            {
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(destination.Length, data.Length - position);
            data.AsMemory(position, count).CopyTo(destination);
            position += count;
            return count;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
