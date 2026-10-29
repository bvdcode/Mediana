using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Wave
{
    public class WaveReaderFactory : IContainerReaderFactory
    {
        private readonly WaveReadOptions options;

        public WaveReaderFactory(WaveReadOptions? options = null)
        {
            this.options = options ?? new WaveReadOptions();
            this.options.Validate();
        }

        public string ImplementationId => "wave.managed";
        public int ProbeLength => 12;
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length >= ProbeLength
            && prefix[..4].SequenceEqual("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WAVE"u8);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) =>
            await WaveReader.OpenAsync(source, allocator, options, cancellationToken).ConfigureAwait(false);
    }
}
