namespace Mediana.Core.Memory
{
    /// <summary>Exclusive writable storage. Publish invalidates this owner and all borrowed writable views.</summary>
    public class WritableMediaBuffer : IDisposable
    {
        private SharedMediaBuffer? storage;

        internal WritableMediaBuffer(SharedMediaBuffer storage) => this.storage = storage;

        public Memory<byte> Memory => GetStorage().Data;

        /// <summary>Transfers ownership to a read-only lease without copying. This owner is not thread-safe.</summary>
        public MediaBuffer Publish()
        {
            SharedMediaBuffer current = GetStorage();
            MediaBuffer result = new(current);
            storage = null;
            return result;
        }

        public void Dispose() => Interlocked.Exchange(ref storage, null)?.Release();

        private SharedMediaBuffer GetStorage() =>
            storage ?? throw new ObjectDisposedException(nameof(WritableMediaBuffer));
    }
}
