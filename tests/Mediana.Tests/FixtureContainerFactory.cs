using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal class FixtureContainerFactory(string id = "fixture.container") : IContainerReaderFactory
    {
        public string ImplementationId => id;
        public int ProbeLength => 4;
        public FixtureContainer? LastReader { get; private set; }
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.StartsWith("TEST"u8);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            using WritableMediaBuffer header = allocator.Allocate(4);
            int offset = 0;
            while (offset < 4)
            {
                int read = await source.ReadAsync(header.Memory[offset..], cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }
                offset += read;
            }
            if (!header.Memory.Span.SequenceEqual("TEST"u8))
            {
                throw new InvalidDataException("The probe bytes were not replayed.");
            }
            LastReader = new FixtureContainer(source, allocator);
            return LastReader;
        }
    }
}
