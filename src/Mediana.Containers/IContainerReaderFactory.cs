using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers
{
    public interface IContainerReaderFactory
    {
        string ImplementationId { get; }
        int ProbeLength { get; }
        bool CanRead(ReadOnlySpan<byte> prefix);
        /// <summary>Borrows the source for the reader lifetime. Failure must release all internal resources.</summary>
        ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default);
    }
}
