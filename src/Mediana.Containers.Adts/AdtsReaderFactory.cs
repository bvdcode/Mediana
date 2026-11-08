// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Adts
{
    public class AdtsReaderFactory : IContainerReaderFactory
    {
        public string ImplementationId => "adts.managed";
        public int ProbeLength => AdtsFrameHeader.ByteLength;
        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length >= ProbeLength && AdtsFrameHeader.HasSync(prefix);

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source is IRandomAccessByteSource random && random.Length is not null)
            {
                return await AdtsIndexedReader.OpenAsync(random, allocator, cancellationToken).ConfigureAwait(false);
            }
            return await AdtsReader.OpenAsync(source, allocator, cancellationToken).ConfigureAwait(false);
        }
    }
}
