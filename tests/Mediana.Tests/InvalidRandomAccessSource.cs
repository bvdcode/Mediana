using Mediana.IO;

namespace Mediana.Tests
{
    internal class InvalidRandomAccessSource(int count) : IRandomAccessByteSource
    {
        public long? Length => null;
        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(count);
        public ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(count);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
