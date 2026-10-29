// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Mp4
{
    public class Mp4ReaderFactory : IContainerReaderFactory
    {
        private readonly Mp4ReadOptions options;

        public Mp4ReaderFactory(Mp4ReadOptions? options = null)
        {
            this.options = options ?? new Mp4ReadOptions();
            this.options.Validate();
        }

        public string ImplementationId => "mp4.managed";
        public int ProbeLength => 12;

        public bool CanRead(ReadOnlySpan<byte> prefix) => prefix.Length >= 8 &&
            Mp4Binary.FourCc(prefix, 4) is "ftyp" or "moov" or "mdat" or "wide" or "free" or "skip";

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source is not IRandomAccessByteSource randomAccess)
            {
                throw new NotSupportedException("MP4 reading requires a random access source.");
            }
            return await Mp4Reader.OpenAsync(randomAccess, allocator, options, cancellationToken).ConfigureAwait(false);
        }
    }
}
