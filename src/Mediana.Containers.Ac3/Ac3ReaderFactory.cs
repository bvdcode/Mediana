// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Ac3
{
    /// <summary>Recognizes big-endian AC-3 and E-AC-3 syncframes independently of file extension.</summary>
    public class Ac3ReaderFactory : IContainerReaderFactory
    {
        public string ImplementationId => "ac3.managed";
        public int ProbeLength => Ac3FrameHeader.HeaderBytes;
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length >= ProbeLength &&
            prefix[0] == 0x0b && prefix[1] == 0x77 && (prefix[5] >> 3 is <= 10 or 16);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source is not IRandomAccessByteSource random || random.Length is null)
            { throw new NotSupportedException("AC-3 indexing requires a known-length random access source."); }
            return await Ac3Reader.OpenAsync(random, allocator, cancellationToken).ConfigureAwait(false);
        }
    }
}
