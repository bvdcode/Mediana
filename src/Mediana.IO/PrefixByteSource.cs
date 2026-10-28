using Mediana.Core.Memory;

namespace Mediana.IO
{
    /// <summary>Replays a probe prefix before reading the source. Retains the prefix; optionally owns the source.</summary>
    public class PrefixByteSource : IByteSource
    {
        private readonly IByteSource source;
        private readonly bool leaveOpen;
        private MediaBuffer? prefix;
        private readonly int prefixLength;
        private int offset;
        private bool disposed;

        public PrefixByteSource(IByteSource source, MediaBuffer prefix, int prefixLength, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(prefix);
            ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, prefix.Memory.Length);
            this.source = source;
            this.leaveOpen = leaveOpen;
            this.prefixLength = prefixLength;
            this.prefix = prefix.Retain();
        }

        public long? Length => source.Length;

        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (destination.IsEmpty)
            {
                return 0;
            }
            if (offset < prefixLength)
            {
                int length = Math.Min(destination.Length, prefixLength - offset);
                prefix!.Memory.Slice(offset, length).CopyTo(destination);
                offset += length;
                if (offset == prefixLength)
                {
                    prefix.Dispose();
                    prefix = null;
                }
                return length;
            }
            return await source.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                prefix?.Dispose();
                prefix = null;
                if (!leaveOpen)
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
