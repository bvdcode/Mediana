using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Flac
{
    public class FlacReaderFactory : IContainerReaderFactory
    {
        private readonly FlacReadOptions options;

        public FlacReaderFactory(FlacReadOptions? options = null)
        {
            this.options = options ?? new FlacReadOptions();
            this.options.Validate();
        }

        public string ImplementationId => "flac.managed";
        public int ProbeLength => 4;
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length >= ProbeLength && prefix[..4].SequenceEqual("fLaC"u8);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) =>
            await FlacReader.OpenAsync(source, allocator, options, cancellationToken).ConfigureAwait(false);
    }
}
