namespace Mediana.IO
{
    public static class ByteSourceExtensions
    {
        public static async ValueTask ReadExactlyAsync(this IByteSource source, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            int read = 0;
            while (read < destination.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = await source.ReadAsync(destination[read..], cancellationToken).ConfigureAwait(false);
                if (count <= 0 || count > destination.Length - read)
                {
                    throw new InvalidDataException("The byte source ended early or returned an invalid read length.");
                }
                read += count;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
