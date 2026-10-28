namespace Mediana.IO
{
    /// <summary>Provides an independent sequential cursor over the origin of a random access source.
    /// Range reads preserve both cursors. Operations and disposal must not overlap.</summary>
    public class RandomAccessCursorByteSource : IRandomAccessByteSource
    {
        private readonly IRandomAccessByteSource source;
        private readonly bool leaveOpen;
        private long position;
        private bool disposed;

        public RandomAccessCursorByteSource(IRandomAccessByteSource source, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(source);
            this.source = source;
            this.leaveOpen = leaveOpen;
        }

        public long? Length => source.Length;

        public async ValueTask<int> ReadAsync(Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            int count = await ReadAtAsync(position, destination, cancellationToken).ConfigureAwait(false);
            position += count;
            return count;
        }

        public async ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (offset > long.MaxValue - destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "The requested range exceeds the offset limit.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            int count = await source.ReadAtAsync(offset, destination, cancellationToken).ConfigureAwait(false);
            if (count < 0 || count > destination.Length)
            {
                throw new InvalidDataException("The source returned an invalid read length.");
            }
            return count;
        }

        public async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                if (!leaveOpen)
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
