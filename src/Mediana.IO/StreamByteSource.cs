namespace Mediana.IO
{
    public class StreamByteSource : IByteSource
    {
        private readonly Stream stream;
        private readonly bool leaveOpen;
        private bool disposed;

        public StreamByteSource(Stream stream, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanRead)
            {
                throw new ArgumentException("The stream must be readable.", nameof(stream));
            }
            this.stream = stream;
            this.leaveOpen = leaveOpen;
            if (stream.CanSeek)
            {
                Length = stream.Length - stream.Position;
            }
        }

        public long? Length { get; }

        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            return await stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                if (!leaveOpen)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
