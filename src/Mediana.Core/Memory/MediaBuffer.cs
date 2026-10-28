namespace Mediana.Core.Memory
{
    /// <summary>A read-only lease. Borrowed memory must not outlive the lease.</summary>
    public class MediaBuffer : IDisposable
    {
        private SharedMediaBuffer? storage;
        private readonly int offset;
        private readonly int length;

        internal MediaBuffer(SharedMediaBuffer storage) : this(storage, 0, storage.Data.Length)
        {
        }

        private MediaBuffer(SharedMediaBuffer storage, int offset, int length)
        {
            this.storage = storage;
            this.offset = offset;
            this.length = length;
        }

        public ReadOnlyMemory<byte> Memory => GetStorage().Data.AsMemory(offset, length);

        /// <summary>Returns an independent lease over the same immutable storage.</summary>
        public MediaBuffer Retain() => Lease(GetStorage(), offset, length);

        /// <summary>Returns an owned view without copying. The allocation budget still counts the complete backing array.</summary>
        public MediaBuffer Slice(int start, int count)
        {
            SharedMediaBuffer current = GetStorage();
            ArgumentOutOfRangeException.ThrowIfNegative(start);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(start, length);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, length - start);
            return Lease(current, offset + start, count);
        }

        private static MediaBuffer Lease(SharedMediaBuffer current, int offset, int length)
        {
            current.Retain();
            bool created = false;
            try
            {
                MediaBuffer result = new(current, offset, length);
                created = true;
                return result;
            }
            finally
            {
                if (!created)
                {
                    current.Release();
                }
            }
        }

        public void Dispose() => Interlocked.Exchange(ref storage, null)?.Release();

        private SharedMediaBuffer GetStorage() =>
            Volatile.Read(ref storage) ?? throw new ObjectDisposedException(nameof(MediaBuffer));
    }
}
