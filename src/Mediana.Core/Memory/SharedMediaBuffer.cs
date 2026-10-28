namespace Mediana.Core.Memory
{
    internal class SharedMediaBuffer(byte[] data, MediaBufferAllocator allocator)
    {
        private int references = 1;
        public byte[] Data { get; } = data;

        public void Retain()
        {
            int current;
            do
            {
                current = Volatile.Read(ref references);
                ObjectDisposedException.ThrowIf(current == 0, this);
                if (current == int.MaxValue)
                {
                    throw new InvalidOperationException("The buffer reference count is exhausted.");
                }
            }
            while (Interlocked.CompareExchange(ref references, current + 1, current) != current);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref references) == 0)
            {
                allocator.Release(Data.Length);
            }
        }
    }
}
