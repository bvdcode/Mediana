namespace Mediana.IO
{
    public static class RandomAccessByteSourceExtensions
    {
        /// <summary>Reads the complete range or throws on EOF. The destination may be partially filled on failure.</summary>
        public static async ValueTask ReadExactlyAtAsync(this IRandomAccessByteSource source, long offset,
            Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (offset > long.MaxValue - destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "The requested range exceeds the offset limit.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            int read = 0;
            while (read < destination.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = await source.ReadAtAsync(offset + read, destination[read..], cancellationToken).ConfigureAwait(false);
                if (count < 0 || count > destination.Length - read)
                {
                    throw new InvalidDataException("The source returned an invalid read length.");
                }
                if (count == 0)
                {
                    throw new EndOfStreamException("The requested byte range is truncated.");
                }
                read += count;
            }
        }
    }
}
