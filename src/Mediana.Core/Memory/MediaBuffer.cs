namespace Mediana.Core.Memory
{
    /// <summary>A read-only lease. Borrowed memory must not outlive the lease.</summary>
    public class MediaBuffer : IDisposable
    {
        private SharedMediaBuffer? storage;

        internal MediaBuffer(SharedMediaBuffer storage) => this.storage = storage;

        public ReadOnlyMemory<byte> Memory => GetStorage().Data;

        /// <summary>Returns an independent lease over the same immutable storage.</summary>
        public MediaBuffer Retain()
        {
            SharedMediaBuffer current = GetStorage();
            current.Retain();
            bool created = false;
            try
            {
                MediaBuffer result = new(current);
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
